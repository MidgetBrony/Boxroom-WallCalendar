using System.Collections;
using System.Globalization;
using System.Text;
using MelonLoader;
using ModsPanel;
using UnityEngine;
using UnityEngine.Networking;

namespace BoxroomWallCalendar;

internal sealed class CalendarEvent
{
    internal string Title = "Untitled event";
    internal string Location = "";
    internal DateTime Start;
    internal DateTime End;
    internal bool AllDay;
    internal string Rule = "";
}

internal static class CalendarFeeds
{
    private static readonly object Gate = new();
    private static readonly List<CalendarEvent> Events = new();
    private static MelonPreferences_Entry<string> feeds;
    private static string status = "No feeds configured";
    private static bool refreshing;
    private static float nextRefresh;
    private static string pendingUrl = "";
    private static int selectedFeed;
    internal static event Action Changed;

    internal static void Initialize()
    {
        MelonPreferences_Category preferences = MelonPreferences.CreateCategory("Boxroom-WallCalendar", "Wall Calendar");
        feeds = preferences.CreateEntry("IcalFeeds", "", "Read-only iCal feed URLs separated by | characters");
        RegisterSettings();
        GameObject host = new("Wall Calendar feeds");
        UnityEngine.Object.DontDestroyOnLoad(host);
        host.hideFlags = HideFlags.HideAndDontSave;
        host.AddComponent<CalendarFeedRuntime>();
        Refresh();
    }

    private static void RegisterSettings()
    {
        List<string> urls = StoredUrls();
        selectedFeed = Mathf.Clamp(selectedFeed, 0, Math.Max(0, urls.Count - 1));
        ModsPanelApi.RegisterSection("MidgetBrony.WallCalendar", "Wall Calendar", 40).Clear()
            .AddText("newIcalFeed", "Calendar URL", () => pendingUrl, value => pendingUrl = value?.Trim() ?? "", "https://.../calendar.ics")
            .AddButton("addIcalFeed", "Add this read-only calendar", "+", AddPendingFeed)
            .AddDropdown("savedIcalFeeds", "Saved calendars", FeedLabels, () => selectedFeed, index => selectedFeed = index)
            .AddButton("removeIcalFeed", "Remove selected calendar", "−", RemoveSelectedFeed)
            .AddButton("refreshIcal", "Update calendar events now", "REFRESH", Refresh)
            .AddLabel("icalPrivacy", urls.Count + " calendar" + (urls.Count == 1 ? "" : "s") + " saved. Feeds are merged, cached locally, and only read.");
    }

    private static List<string> StoredUrls() => (feeds?.Value ?? "").Split('|').Select(value => value.Trim())
        .Where(value => Uri.TryCreate(value, UriKind.Absolute, out Uri uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
        .Distinct(StringComparer.Ordinal).ToList();

    private static IReadOnlyList<string> FeedLabels()
    {
        List<string> urls = StoredUrls();
        if (urls.Count == 0) return new[] { "No calendars added" };
        return urls.Select((url, index) => "Calendar " + (index + 1) + " — " + new Uri(url).Host).ToArray();
    }

    private static void AddPendingFeed()
    {
        if (!Uri.TryCreate(pendingUrl, UriKind.Absolute, out Uri uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
        {
            ModsUi.ShowToast("Enter a valid HTTP or HTTPS iCal URL");
            return;
        }
        List<string> urls = StoredUrls();
        if (urls.Contains(pendingUrl, StringComparer.Ordinal)) { ModsUi.ShowToast("That calendar is already saved"); return; }
        urls.Add(pendingUrl);
        feeds.Value = string.Join("|", urls);
        pendingUrl = "";
        selectedFeed = urls.Count - 1;
        MelonPreferences.Save();
        ClearCache();
        RegisterSettings();
        Refresh();
        ModsUi.ShowToast("Calendar added");
    }

    private static void RemoveSelectedFeed()
    {
        List<string> urls = StoredUrls();
        if (urls.Count == 0) { ModsUi.ShowToast("No calendar is selected"); return; }
        selectedFeed = Mathf.Clamp(selectedFeed, 0, urls.Count - 1);
        urls.RemoveAt(selectedFeed);
        feeds.Value = string.Join("|", urls);
        selectedFeed = Mathf.Clamp(selectedFeed, 0, Math.Max(0, urls.Count - 1));
        MelonPreferences.Save();
        ClearCache();
        RegisterSettings();
        Refresh();
        ModsUi.ShowToast("Calendar removed");
    }

    private static void ClearCache()
    {
        try
        {
            string directory = Path.Combine(Application.persistentDataPath, "WallCalendar", "CalendarCache");
            if (Directory.Exists(directory)) foreach (string file in Directory.GetFiles(directory, "feed-*.ics")) File.Delete(file);
        }
        catch { }
    }

    internal static void Refresh()
    {
        if (refreshing) return;
        MelonCoroutines.Start(RefreshRoutine());
    }

    internal static bool HasEvents(DateTime day) => EventsFor(day).Count != 0;

    internal static List<CalendarEvent> EventsFor(DateTime day)
    {
        DateTime date = day.Date;
        List<CalendarEvent> result = new();
        lock (Gate)
        {
            foreach (CalendarEvent item in Events)
                if (OccursOn(item, date)) result.Add(item);
        }
        return result.GroupBy(e => $"{e.Title}\n{OccurrenceStart(e, date):O}\n{e.Location}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).OrderBy(e => OccurrenceStart(e, date)).ToList();
    }

    internal static string Describe(CalendarEvent item, DateTime day)
    {
        if (item.AllDay) return string.IsNullOrWhiteSpace(item.Location) ? "ALL DAY" : "ALL DAY  •  " + item.Location;
        DateTime occurrence = OccurrenceStart(item, day.Date);
        string text = occurrence.ToString("t", CultureInfo.CurrentCulture);
        return string.IsNullOrWhiteSpace(item.Location) ? text : text + "  •  " + item.Location;
    }

    private static IEnumerator RefreshRoutine()
    {
        refreshing = true;
        string[] urls = StoredUrls().ToArray();
        if (urls.Length == 0)
        {
            lock (Gate) Events.Clear();
            status = "No feeds configured";
            refreshing = false;
            Changed?.Invoke();
            yield break;
        }

        status = "Refreshing " + urls.Length + " feed" + (urls.Length == 1 ? "" : "s") + "…";
        List<CalendarEvent> loaded = new();
        int successful = 0;
        string cacheDirectory = Path.Combine(Application.persistentDataPath, "WallCalendar", "CalendarCache");
        Directory.CreateDirectory(cacheDirectory);
        for (int index = 0; index < urls.Length; index++)
        {
            string body = null;
            string cache = Path.Combine(cacheDirectory, "feed-" + index + ".ics");
            using (UnityWebRequest request = UnityWebRequest.Get(urls[index]))
            {
                request.timeout = 20;
                request.SetRequestHeader("User-Agent", "BOXROOM-WallCalendar/1.3");
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    body = request.downloadHandler.text;
                    try { File.WriteAllText(cache, body, Encoding.UTF8); } catch { }
                    successful++;
                }
            }
            if (body == null) try { if (File.Exists(cache)) body = File.ReadAllText(cache); } catch { }
            if (!string.IsNullOrWhiteSpace(body)) loaded.AddRange(Parse(body));
        }
        lock (Gate) { Events.Clear(); Events.AddRange(loaded); }
        status = successful + "/" + urls.Length + " feeds refreshed • " + loaded.Count + " events";
        refreshing = false;
        nextRefresh = Time.unscaledTime + 1800f;
        Changed?.Invoke();
    }

    private static List<CalendarEvent> Parse(string text)
    {
        List<string> lines = new();
        foreach (string raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            if ((raw.StartsWith(" ") || raw.StartsWith("\t")) && lines.Count > 0) lines[^1] += raw.Substring(1); else lines.Add(raw);
        List<CalendarEvent> result = new();
        CalendarEvent current = null;
        foreach (string line in lines)
        {
            if (line == "BEGIN:VEVENT") { current = new CalendarEvent(); continue; }
            if (line == "END:VEVENT") { if (current != null && current.Start != default) result.Add(current); current = null; continue; }
            if (current == null) continue;
            int colon = line.IndexOf(':'); if (colon < 0) continue;
            string key = line.Substring(0, colon), value = line.Substring(colon + 1);
            string name = key.Split(';')[0].ToUpperInvariant();
            if (name == "SUMMARY") current.Title = Decode(value);
            else if (name == "LOCATION") current.Location = Decode(value);
            else if (name == "RRULE") current.Rule = value;
            else if (name == "DTSTART") { current.AllDay = key.IndexOf("VALUE=DATE", StringComparison.OrdinalIgnoreCase) >= 0 || value.Length == 8; current.Start = ParseDate(value, current.AllDay); }
            else if (name == "DTEND") current.End = ParseDate(value, key.IndexOf("VALUE=DATE", StringComparison.OrdinalIgnoreCase) >= 0 || value.Length == 8);
        }
        foreach (CalendarEvent item in result) if (item.End == default) item.End = item.AllDay ? item.Start.AddDays(1) : item.Start.AddHours(1);
        return result;
    }

    private static DateTime ParseDate(string value, bool allDay)
    {
        value = value.Trim(); bool utc = value.EndsWith("Z", StringComparison.OrdinalIgnoreCase);
        if (utc) value = value.Substring(0, value.Length - 1);
        string[] formats = allDay ? new[] { "yyyyMMdd" } : new[] { "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm" };
        if (!DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)) return default;
        return utc ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc).ToLocalTime() : DateTime.SpecifyKind(parsed, DateTimeKind.Local);
    }

    private static string Decode(string value) => value.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim();

    private static bool OccursOn(CalendarEvent item, DateTime day)
    {
        if (string.IsNullOrWhiteSpace(item.Rule)) return day >= item.Start.Date && day < item.End.Date.AddDays(item.End.TimeOfDay == TimeSpan.Zero ? 0 : 1);
        Dictionary<string,string> rule = item.Rule.Split(';').Select(part => part.Split(new[]{'='},2)).Where(parts => parts.Length==2).ToDictionary(parts=>parts[0].ToUpperInvariant(),parts=>parts[1]);
        if (rule.TryGetValue("UNTIL", out string untilText) && ParseDate(untilText, untilText.Length == 8).Date < day) return false;
        if (day < item.Start.Date) return false;
        int interval = rule.TryGetValue("INTERVAL", out string intervalText) && int.TryParse(intervalText, out int parsed) ? Math.Max(1, parsed) : 1;
        string frequency = rule.TryGetValue("FREQ", out string frequencyText) ? frequencyText : "";
        bool matches = MatchesRuleDate(item, day, rule, frequency, interval);
        if (!matches) return false;
        if (rule.TryGetValue("COUNT", out string countText) && int.TryParse(countText, out int maximum) && maximum > 0)
        {
            int seen = 0;
            for (DateTime candidate = item.Start.Date; candidate <= day; candidate = candidate.AddDays(1))
            {
                if (!MatchesRuleDate(item, candidate, rule, frequency, interval)) continue;
                seen++;
                if (candidate == day) return seen <= maximum;
                if (seen >= maximum) return false;
            }
            return false;
        }
        return true;
    }

    private static bool MatchesRuleDate(CalendarEvent item, DateTime day, Dictionary<string,string> rule, string frequency, int interval)
    {
        if (frequency == "DAILY") return (day - item.Start.Date).Days % interval == 0;
        if (frequency == "WEEKLY")
        {
            if (((day - item.Start.Date).Days / 7) % interval != 0) return false;
            string code = day.DayOfWeek.ToString().Substring(0,2).ToUpperInvariant();
            return rule.TryGetValue("BYDAY", out string days) ? days.Split(',').Any(value => value.EndsWith(code, StringComparison.Ordinal)) : day.DayOfWeek == item.Start.DayOfWeek;
        }
        if (frequency == "MONTHLY") return ((day.Year-item.Start.Year)*12+day.Month-item.Start.Month)%interval==0 && day.Day==item.Start.Day;
        if (frequency == "YEARLY") return (day.Year-item.Start.Year)%interval==0 && day.Month==item.Start.Month && day.Day==item.Start.Day;
        return false;
    }

    private static DateTime OccurrenceStart(CalendarEvent item, DateTime day) => day.Date.Add(item.Start.TimeOfDay);

    internal static void Tick()
    {
        if (!refreshing && nextRefresh > 0f && Time.unscaledTime >= nextRefresh) Refresh();
    }
}

internal sealed class CalendarFeedRuntime : MonoBehaviour
{
    private void Update() => CalendarFeeds.Tick();
}
