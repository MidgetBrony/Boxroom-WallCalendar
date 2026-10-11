using System.Reflection;
using SkiaSharp;
using UnityEngine;

namespace BoxroomWallCalendar;

internal enum CalendarFontWeight { Regular, Medium, SemiBold }

internal sealed class CalendarTextCanvas : IDisposable
{
    private static SKTypeface regular;
    private static SKTypeface medium;
    private static SKTypeface semiBold;
    private readonly Texture2D target;
    private readonly SKBitmap bitmap;
    private readonly SKCanvas canvas;

    internal static void Initialize()
    {
        regular ??= LoadTypeface("Inter-Regular.ttf");
        medium ??= LoadTypeface("Inter-Medium.ttf");
        semiBold ??= LoadTypeface("Inter-SemiBold.ttf");
    }

    internal CalendarTextCanvas(Texture2D texture)
    {
        Initialize();
        target = texture;
        bitmap = new SKBitmap(texture.width, texture.height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        Color32[] pixels = texture.GetPixels32();
        for (int y = 0; y < texture.height; y++)
        for (int x = 0; x < texture.width; x++)
        {
            Color32 color = pixels[y * texture.width + x];
            bitmap.SetPixel(x, texture.height - 1 - y, new SKColor(color.r, color.g, color.b, color.a));
        }
        canvas = new SKCanvas(bitmap);
    }

    internal void Center(string text, float x, float centerY, float size, Color32 color, CalendarFontWeight weight)
        => Draw(text, x, centerY, size, color, weight, SKTextAlign.Center);

    internal void Right(string text, float x, float centerY, float size, Color32 color, CalendarFontWeight weight)
        => Draw(text, x, centerY, size, color, weight, SKTextAlign.Right);

    private void Draw(string text, float x, float centerY, float size, Color32 color, CalendarFontWeight weight, SKTextAlign align)
    {
        using var font = new SKFont(Typeface(weight), size) { Subpixel = true };
        using var paint = new SKPaint { Color = new SKColor(color.r, color.g, color.b, color.a), IsAntialias = true };
        SKFontMetrics metrics = font.Metrics;
        float skCenter = target.height - centerY;
        float baseline = skCenter - (metrics.Ascent + metrics.Descent) * .5f;
        canvas.DrawText(text ?? "", x, baseline, align, font, paint);
    }

    internal void Apply()
    {
        canvas.Flush();
        Color32[] pixels = new Color32[target.width * target.height];
        for (int y = 0; y < target.height; y++)
        for (int x = 0; x < target.width; x++)
        {
            SKColor color = bitmap.GetPixel(x, target.height - 1 - y);
            pixels[y * target.width + x] = new Color32(color.Red, color.Green, color.Blue, color.Alpha);
        }
        target.SetPixels32(pixels);
    }

    public void Dispose() { canvas.Dispose(); bitmap.Dispose(); }

    private static SKTypeface Typeface(CalendarFontWeight weight) => weight switch
    {
        CalendarFontWeight.SemiBold => semiBold ?? SKTypeface.Default,
        CalendarFontWeight.Medium => medium ?? regular ?? SKTypeface.Default,
        _ => regular ?? SKTypeface.Default
    };

    private static SKTypeface LoadTypeface(string suffix)
    {
        Assembly assembly = typeof(CalendarTextCanvas).Assembly;
        string resource = assembly.GetManifestResourceNames().FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (resource == null) return SKTypeface.Default;
        using Stream stream = assembly.GetManifestResourceStream(resource);
        if (stream == null) return SKTypeface.Default;
        using MemoryStream copy = new(); stream.CopyTo(copy);
        using SKData data = SKData.CreateCopy(copy.ToArray());
        return SKTypeface.FromData(data) ?? SKTypeface.Default;
    }
}
