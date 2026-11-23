using System.Windows.Threading;

namespace ChurchSlides.Spike;

/// <summary>
/// One STA thread with its own Dispatcher (ARCH 4.1), so WPF can build visuals and run
/// RenderTargetBitmap off the UI thread. Below-normal priority: on a 2-core CPU it must not
/// compete with the UI thread, which ticks the crossfade animation.
/// </summary>
public sealed class RenderThread : IDisposable
{
    private readonly Dispatcher dispatcher;

    public RenderThread()
    {
        var ready = new TaskCompletionSource<Dispatcher>();
        var thread = new Thread(() =>
        {
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        })
        {
            Name = "Render",
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        dispatcher = ready.Task.Result;
    }

    public Task<T> Run<T>(Func<T> work) => dispatcher.InvokeAsync(work).Task;

    public void Dispose() => dispatcher.InvokeShutdown();
}

/// <summary>Where slides are pre-rendered: on the UI thread, or on the render thread.</summary>
public sealed class PreRenderer(SlideFactory factory, RenderThread? thread)
{
    public string Where => thread is not null ? "render thread" : "UI thread";

    /// <summary>
    /// Renders a slide. On the UI thread it runs at Background priority, as a real UI-thread
    /// pre-render would, so it falls between animation frames and stalls whatever comes next.
    /// </summary>
    public Task<RenderedSlide> Render(SlideSpec spec) =>
        thread is not null
            ? thread.Run(() => factory.Render(spec))
            : Dispatcher.CurrentDispatcher.InvokeAsync(() => factory.Render(spec), DispatcherPriority.Background).Task;

    /// <summary>Renders each slide once, one after the other, with nothing else going on, and drops it.</summary>
    public async Task<List<RenderTiming>> RenderIdle(IEnumerable<SlideSpec> specs)
    {
        var all = new List<RenderTiming>();
        foreach (var spec in specs)
            all.Add((await Render(spec)).Timing);
        return all;
    }

    /// <summary>Whole-slide, text-layer and background-decode times.</summary>
    public static string Timing(IReadOnlyCollection<RenderTiming> slides) =>
        $"slide {Summary.Of(slides.Select(s => s.TotalMs))}; text layer {Summary.Of(slides.Select(s => s.TextMs))}; " +
        $"background decode and placing {Summary.Of(slides.Where(s => s.DecodedBackground).Select(s => s.BackgroundMs))}";
}
