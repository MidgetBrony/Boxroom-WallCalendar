using System.Reflection;
using UnityEngine;

namespace BoxroomWallCalendar;

internal static class CalendarDoodles
{
    private const string DefaultAssetResourcePrefix = "BoxroomWallCalendar.DefaultAssets";
    private static Texture2D blackCircle;
    private static Texture2D redCircle;
    private static readonly Dictionary<string, Texture2D> Holidays = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Texture2D, RectInt> VisibleBounds = new();

    internal static void Initialize()
    {
        string root = Path.Combine(Application.persistentDataPath, "WallCalendar", "Assets");
        string circles = Path.Combine(root, "Circles");
        string holidays = Path.Combine(root, "Holidays");
        Directory.CreateDirectory(circles);
        Directory.CreateDirectory(holidays);
        SeedBundledAsset(Path.Combine(circles, "Black.png"), "Circles.Black.png");
        SeedBundledAsset(Path.Combine(circles, "Red.png"), "Circles.Red.png");
        SeedBundledAsset(Path.Combine(holidays, "Halloween.png"), "Holidays.Halloween.png");
        SeedBundledAsset(Path.Combine(holidays, "Thanksgiving.png"), "Holidays.Thanksgiving.png");
        blackCircle = Load(Path.Combine(circles, "Black.png"));
        redCircle = Load(Path.Combine(circles, "Red.png"));
        Holidays.Clear();
        foreach (string file in Directory.GetFiles(holidays, "*.png"))
        {
            Texture2D image = Load(file);
            if (image != null) Holidays[Normalize(Path.GetFileNameWithoutExtension(file))] = image;
        }
    }

    private static void SeedBundledAsset(string destination, string resourceSuffix)
    {
        if (File.Exists(destination)) return;
        string temporary = destination + ".tmp";
        try
        {
            using Stream resource = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(DefaultAssetResourcePrefix + "." + resourceSuffix);
            if (resource == null)
            {
                MelonLoader.MelonLogger.Warning("[Wall Calendar] Missing bundled artwork resource: " + resourceSuffix);
                return;
            }
            if (File.Exists(temporary)) File.Delete(temporary);
            using (FileStream output = File.Create(temporary)) resource.CopyTo(output);
            File.Move(temporary, destination);
        }
        catch (Exception error)
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            MelonLoader.MelonLogger.Warning("[Wall Calendar] Could not install default artwork '" + resourceSuffix + "': " + error.Message);
        }
    }

    internal static void DrawDateMark(Texture2D canvas, DateTime day, int centerX, int centerY, int circleWidth, int circleHeight)
    {
        List<CalendarEvent> events = CalendarFeeds.EventsFor(day);
        if (events.Count == 0) return;
        Texture2D holiday = FindHoliday(events);
        Texture2D circle = holiday != null ? redCircle : blackCircle;
        System.Random random = new(day.Year * 10000 + day.Month * 100 + day.Day);
        float angle = (float)(random.NextDouble() * 12d - 6d);
        int offsetX = random.Next(-1, 2), offsetY = random.Next(-1, 2);
        if (circle != null) Overlay(canvas, circle, centerX + offsetX, centerY + offsetY, circleWidth, circleHeight, angle);
        if (holiday != null)
        {
            int size = Math.Max(18, circleHeight * 3 / 4);
            Overlay(canvas, holiday, centerX + circleWidth / 2 - 2, centerY + circleHeight / 2 + 1,
                size, Mathf.RoundToInt(size * ((float)holiday.height / holiday.width)), -angle * .45f);
        }
    }

    private static Texture2D FindHoliday(IEnumerable<CalendarEvent> events)
    {
        foreach (CalendarEvent item in events)
        {
            string title = Normalize(item.Title);
            if (Holidays.TryGetValue(title, out Texture2D exact)) return exact;
            if (title.EndsWith("day", StringComparison.Ordinal) && Holidays.TryGetValue(title.Substring(0, title.Length - 3), out Texture2D dayMatch)) return dayMatch;
        }
        return null;
    }

    private static string Normalize(string value) => new((value ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static Texture2D Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            Texture2D texture = new(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path), false)) { UnityEngine.Object.Destroy(texture); return null; }
            texture.name = "Wall Calendar " + Path.GetFileNameWithoutExtension(path);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            VisibleBounds[texture] = FindVisibleBounds(texture);
            return texture;
        }
        catch { return null; }
    }

    private static void Overlay(Texture2D canvas, Texture2D source, int centerX, int centerY, int width, int height, float degrees)
    {
        if (canvas == null || source == null || width <= 0 || height <= 0) return;
        RectInt bounds = VisibleBounds.TryGetValue(source, out RectInt found) ? found : new RectInt(0, 0, source.width, source.height);
        float radians = degrees * Mathf.Deg2Rad, cosine = Mathf.Cos(radians), sine = Mathf.Sin(radians);
        int halfWidth = width / 2 + 3, halfHeight = height / 2 + 3;
        for (int y = -halfHeight; y <= halfHeight; y++)
        for (int x = -halfWidth; x <= halfWidth; x++)
        {
            float sourceX = cosine * x + sine * y;
            float sourceY = -sine * x + cosine * y;
            float u = sourceX / width + .5f, v = sourceY / height + .5f;
            if (u < 0f || u > 1f || v < 0f || v > 1f) continue;
            int destinationX = centerX + x, destinationY = centerY + y;
            if (destinationX < 0 || destinationX >= canvas.width || destinationY < 0 || destinationY >= canvas.height) continue;
            // Preserve very thin pencil strokes while reducing a large drawing to
            // a tiny date mark. A single bilinear sample often lands between the
            // original strokes and makes the circle nearly disappear.
            Color ink = Color.clear;
            const int samples = 10;
            for (int sy = 0; sy < samples; sy++)
            for (int sx = 0; sx < samples; sx++)
            {
                float sampleU = (bounds.xMin + (u + ((sx + .5f) / samples - .5f) / width) * bounds.width) / source.width;
                float sampleV = (bounds.yMin + (v + ((sy + .5f) / samples - .5f) / height) * bounds.height) / source.height;
                Color candidate = source.GetPixelBilinear(Mathf.Clamp01(sampleU), Mathf.Clamp01(sampleV));
                if (candidate.a > ink.a) ink = candidate;
            }
            if (ink.a < .015f) continue;
            Color paper = canvas.GetPixel(destinationX, destinationY);
            float strength = Mathf.Clamp01(ink.a * 3.25f);
            ink.a = paper.a;
            canvas.SetPixel(destinationX, destinationY, Color.Lerp(paper, ink, strength));
        }
    }

    private static RectInt FindVisibleBounds(Texture2D texture)
    {
        int minX=texture.width,minY=texture.height,maxX=-1,maxY=-1;
        Color32[] pixels=texture.GetPixels32();
        for(int y=0;y<texture.height;y++)for(int x=0;x<texture.width;x++)
        {
            if(pixels[y*texture.width+x].a<4) continue;
            if(x<minX)minX=x;if(x>maxX)maxX=x;if(y<minY)minY=y;if(y>maxY)maxY=y;
        }
        if(maxX<minX||maxY<minY)return new RectInt(0,0,texture.width,texture.height);
        int padding=Math.Max(1,Math.Min(texture.width,texture.height)/100);
        minX=Math.Max(0,minX-padding);minY=Math.Max(0,minY-padding);
        maxX=Math.Min(texture.width-1,maxX+padding);maxY=Math.Min(texture.height-1,maxY+padding);
        return new RectInt(minX,minY,maxX-minX+1,maxY-minY+1);
    }
}
