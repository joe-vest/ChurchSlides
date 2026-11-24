using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime;

namespace ChurchSlides.Spike;

/// <summary>One launch's startup figures (S7).</summary>
public sealed record StartupRun(
    DateTimeOffset At,
    string Build,
    bool Kestrel,
    double UptimeMinutes,
    double ToMainMs,
    double KestrelMs,
    double ToFirstFrameMs,
    long JitMethods,
    double JitMs);

/// <summary>Times this launch from process start to the main window's first ContentRendered.</summary>
public static class Startup
{
    private static readonly Stopwatch SinceMain = new();
    private static double toMainMs;
    private static double kestrelMs;

    public static bool KestrelRequested { get; private set; }
    public static bool AutoRequested { get; private set; }

    /// <summary>--auto-s3: only the rich-text run (S3), which needs no second monitor or media.</summary>
    public static bool AutoS3Requested { get; private set; }

    /// <summary>--auto-s4: only the audio run (S4), which needs MP3s in the media folder but no second monitor.</summary>
    public static bool AutoS4Requested { get; private set; }
    public static string? KestrelUrl { get; private set; }

    public static void MainEntered(string[] args)
    {
        SinceMain.Start();
        var processStart = Process.GetCurrentProcess().StartTime;
        toMainMs = (DateTime.Now - processStart).TotalMilliseconds;
        KestrelRequested = args.Any(a => a.Equals("--kestrel", StringComparison.OrdinalIgnoreCase));
        AutoRequested = args.Any(a => a.Equals("--auto", StringComparison.OrdinalIgnoreCase));
        AutoS3Requested = args.Any(a => a.Equals("--auto-s3", StringComparison.OrdinalIgnoreCase));
        AutoS4Requested = args.Any(a => a.Equals("--auto-s4", StringComparison.OrdinalIgnoreCase));
    }

    public static void StartKestrelIfRequested()
    {
        if (!KestrelRequested)
            return;
        var sw = Stopwatch.StartNew();
        KestrelUrl = KestrelHost.Start();
        kestrelMs = sw.Elapsed.TotalMilliseconds;
    }

    public static StartupRun FirstFrame() => new(
        DateTimeOffset.Now,
        BuildFlavor(),
        KestrelRequested,
        Environment.TickCount64 / 60000.0,
        toMainMs,
        kestrelMs,
        toMainMs + SinceMain.Elapsed.TotalMilliseconds,
        JitInfo.GetCompiledMethodCount(),
        JitInfo.GetCompilationTime().TotalMilliseconds);

    /// <summary>"ReadyToRun" if this assembly carries precompiled code, else "plain" (JIT only).</summary>
    public static string BuildFlavor()
    {
        try
        {
            using var stream = File.OpenRead(typeof(Startup).Assembly.Location);
            using var pe = new PEReader(stream);
            var r2r = pe.PEHeaders.CorHeader?.ManagedNativeHeaderDirectory.Size > 0;
            return r2r ? "ReadyToRun" : "plain";
        }
        catch (IOException)
        {
            return "unknown";
        }
    }
}
