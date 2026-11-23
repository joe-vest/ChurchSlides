using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ChurchSlides.Spike;

/// <summary>How a background fills the canvas (SLD-006).</summary>
public enum FitMode
{
    /// <summary>Whole image visible, black bars where the shapes differ.</summary>
    Fit,

    /// <summary>Canvas covered, image cropped where the shapes differ.</summary>
    Fill,

    /// <summary>Canvas covered, image distorted.</summary>
    Stretch,
}

public sealed record SlideSpec(string Text, string BackgroundPath, FitMode Fit);

/// <summary>
/// A pre-rendered slide (ARCH 7.2): two frozen bitmaps at output size, so Clear can show the
/// background alone.
/// </summary>
public sealed record RenderedSlide(BitmapSource Background, BitmapSource Text, RenderTiming Timing);

/// <summary>How long a slide took, on whichever thread the factory ran. Kept apart from the bitmaps so figures don't hold them.</summary>
public sealed record RenderTiming(bool DecodedBackground, double BackgroundMs, double TextMs)
{
    public double TotalMs => BackgroundMs + TextMs;
}

/// <summary>
/// Builds slides at one output size. Backgrounds are decoded straight to output size
/// (SLD-007) and kept in a small cache, because many slides share one. Not thread-safe:
/// use one factory per thread. The results are frozen, so any thread can show them.
/// </summary>
public sealed class SlideFactory(int width, int height)
{
    /// <summary>About 30 MB at 1440×900: a handful of distinct backgrounds per Service window.</summary>
    private const int CacheEntries = 6;

    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Brush Shadow = Frozen(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)));

    private readonly List<(string Key, BitmapSource Bitmap)> cache = [];

    public RenderedSlide Render(SlideSpec spec)
    {
        var clock = Stopwatch.StartNew();
        var background = CachedBackground(spec, out var decoded);
        var backgroundMs = clock.Elapsed.TotalMilliseconds;

        clock.Restart();
        var text = TextLayer(spec.Text);
        return new RenderedSlide(background, text, new RenderTiming(decoded, backgroundMs, clock.Elapsed.TotalMilliseconds));
    }

    /// <summary>Background and text in one bitmap, for thumbnails.</summary>
    public BitmapSource RenderFlat(SlideSpec spec)
    {
        var visual = NewVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(CachedBackground(spec, out _), new Rect(0, 0, width, height));
            DrawText(dc, spec.Text);
        }
        return Rasterize(visual);
    }

    private BitmapSource CachedBackground(SlideSpec spec, out bool decoded)
    {
        var key = $"{spec.Fit}|{spec.BackgroundPath}";
        var index = cache.FindIndex(e => e.Key == key);
        decoded = index < 0;
        BitmapSource background;
        if (index >= 0)
        {
            background = cache[index].Bitmap;
            cache.RemoveAt(index);
        }
        else
        {
            background = Background(spec.BackgroundPath, spec.Fit);
            if (cache.Count == CacheEntries)
                cache.RemoveAt(0);
        }
        cache.Add((key, background)); // most recently used last
        return background;
    }

    /// <summary>The image decoded at output size and placed on a black canvas by the fit mode.</summary>
    public BitmapSource Background(string path, FitMode fit)
    {
        var visual = NewVisual();
        using (var dc = visual.RenderOpen())
            DrawBackground(dc, path, fit);
        return Rasterize(visual);
    }

    /// <summary>The image decoded at the size it will be drawn (never above its own size). Frozen.</summary>
    public BitmapSource Decode(string path, FitMode fit)
    {
        var (rect, source) = Placement(path, fit);
        return Decode(path, rect.Size, source);
    }

    private BitmapSource TextLayer(string text)
    {
        var visual = NewVisual();
        using (var dc = visual.RenderOpen())
            DrawText(dc, text);
        return Rasterize(visual);
    }

    /// <summary>The image's pixel size, read from its header only.</summary>
    public static (int Width, int Height) PixelSize(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }

    private void DrawBackground(DrawingContext dc, string path, FitMode fit)
    {
        var (rect, source) = Placement(path, fit);
        dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
        dc.DrawImage(Decode(path, rect.Size, source), rect);
        dc.Pop();
    }

    /// <summary>Where the image goes on the canvas in pixels, and the image's own size.</summary>
    private (Rect Rect, (int Width, int Height) Source) Placement(string path, FitMode fit)
    {
        var (iw, ih) = PixelSize(path);
        if (fit == FitMode.Stretch)
            return (new Rect(0, 0, width, height), (iw, ih));
        var scale = fit == FitMode.Fit
            ? Math.Min((double)width / iw, (double)height / ih)
            : Math.Max((double)width / iw, (double)height / ih);
        double w = Math.Round(iw * scale), h = Math.Round(ih * scale);
        return (new Rect((width - w) / 2, (height - h) / 2, w, h), (iw, ih));
    }

    private static BitmapSource Decode(string path, Size size, (int Width, int Height) source)
    {
        var (iw, ih) = source;
        using var stream = File.OpenRead(path);
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = stream; // a stream, not a URI, so WPF's own image cache can't skew timings
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.DecodePixelWidth = (int)Math.Min(size.Width, iw);
        image.DecodePixelHeight = (int)Math.Min(size.Height, ih);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void DrawText(DrawingContext dc, string text)
    {
        var size = height / 15.0;
        var maxWidth = width * 0.86;
        FormattedText Make(Brush brush) =>
            new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush, 1.0)
            {
                MaxTextWidth = maxWidth,
                TextAlignment = TextAlignment.Center,
                LineHeight = size * 1.25,
            };

        var white = Make(Brushes.White);
        var origin = new Point((width - maxWidth) / 2, (height - white.Height) / 2);
        var offset = Math.Max(1, height / 450.0);
        dc.DrawText(Make(Shadow), new Point(origin.X + offset, origin.Y + offset));
        dc.DrawText(white, origin);
    }

    private static DrawingVisual NewVisual()
    {
        var visual = new DrawingVisual();
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale); // reads better on projectors (ARCH 7.2)
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        return visual;
    }

    private BitmapSource Rasterize(Visual visual)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
