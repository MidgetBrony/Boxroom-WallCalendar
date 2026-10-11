# BOXROOM Wall Calendar

A native wall-mounted calendar for BOXROOM. It shows the current month using your computer's regional date settings, crosses off completed days, and can display events from read-only iCal feeds such as Google Calendar.

Wall Calendar appears in BOXROOM's built-in **Mods** catalogue. It does not create a separate catalogue and does not require LocalWorkshop.

## Requirements

- BOXROOM with MelonLoader 0.7.0 or newer
- ModsPanel 2.6.2 or newer

## Installation

The recommended installation method is BoxMate. For manual installation, copy `Boxroom_WallCalendar.dll` into BOXROOM's `Mods` folder.

Start BOXROOM, open the built-in **Mods** catalogue, select **Wall Calendar**, and place it against a wall. It uses BOXROOM's native wall-object placement preset.

## Add a Google Calendar

Wall Calendar only reads events. It cannot add, edit, or delete anything in Google Calendar.

1. On a computer, open [Google Calendar](https://calendar.google.com/).
2. Select the gear icon, then **Settings**.
3. Under **Settings for my calendars**, select the calendar you want to display.
4. Open **Integrate calendar**.
5. Copy the **Secret address in iCal format**. The address normally ends in `.ics`.
6. In BOXROOM, open **Mod Settings → Wall Calendar**.
7. Paste the address into **Calendar URL** and press **+**.
8. Press **REFRESH** if the events do not appear immediately.

Google documents the same read-only iCal procedure in [Sync your calendar with computer programs](https://support.google.com/calendar/answer/37648?hl=en). Treat the secret iCal address like a password. Anyone who has it can read that calendar. If it is exposed, use Google's **Reset** option to invalidate it. Work or school administrators may disable secret iCal addresses.

You can add more than one feed. Wall Calendar merges them, refreshes them every 30 minutes, and keeps a local cache for offline use. The settings screen identifies saved feeds only by calendar number and hostname; it does not display the private URL path.

## Calendar artwork

On first launch, the mod installs its bundled artwork without replacing files you already have:

```text
%USERPROFILE%\AppData\LocalLow\NestedLoop\BOXROOM\WallCalendar\Assets\
├── Circles\
│   ├── Black.png
│   └── Red.png
└── Holidays\
    ├── Halloween.png
    └── Thanksgiving.png
```

Dates containing ordinary events use `Circles\Black.png`. A date with matching holiday artwork uses `Circles\Red.png` and displays the holiday doodle beside the date.

### Add custom date artwork

Place a transparent PNG in the `Holidays` folder and name it after an event title:

- `Halloween.png` matches an event named `Halloween`.
- `Christmas.png` matches `Christmas` and `Christmas Day`.
- `Rustys Birthday.png` matches `Rusty's Birthday`.

Matching ignores capitalization, spaces, and punctuation. Transparent PNGs work best; image dimensions are flexible because the mod crops transparent margins and scales the visible drawing. Restart BOXROOM after adding or replacing artwork.

The bundled files are installed only when missing, so personal replacements are preserved across launches and mod updates.

## Header picture

The large picture above the dates uses BOXROOM's native `GameImagePainter`:

- Look at a placed calendar with empty hands and use **Secondary → Add Custom Image** to choose a local picture.
- While holding a game or album, use BOXROOM's normal paint-art control to apply and cycle its screenshots or extra artwork.

Images are center-cropped to the calendar's 2:1 header without stretching. The selection is stored through BOXROOM's normal placeable save data.

## Viewing events

Dates with events receive a small hand-drawn circle. Use **Primary** on a placed calendar to open the enlarged month view, then select a marked date to see its event titles, times, and locations. Common daily, weekly, monthly, and yearly iCal recurrence rules are supported.

## Build

Copy `Directory.Build.user.props.example` to `Directory.Build.user.props`, adjust the paths, then run:

```powershell
dotnet build .\Boxroom-WallCalendar.csproj -c Release
```

The project targets .NET Standard 2.1 for BOXROOM and MelonLoader compatibility.

## Validation boundary

Compilation and package verification do not prove in-game placement, artwork appearance, event rendering, or save restoration. Those behaviors require a live BOXROOM test.
