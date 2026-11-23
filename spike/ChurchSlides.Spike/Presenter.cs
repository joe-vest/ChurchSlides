using System.Globalization;

namespace ChurchSlides.Spike;

/// <summary>What one transition measured. Times in ms.</summary>
public sealed record TransitionResult(
    int FadeMs,
    FrameStats Frames,
    double InputToFrameMs,
    double OnDemandMs,
    IReadOnlyList<RenderTiming> PreRendered)
{
    public override string ToString()
    {
        var c = CultureInfo.InvariantCulture;
        var text = FadeMs > 0
            ? string.Create(c, $"Crossfade {FadeMs} ms: {Frames}.")
            : string.Create(c, $"Cut: input to frame {InputToFrameMs:F0} ms.");
        if (OnDemandMs > 0)
            text += string.Create(c, $" Not pre-rendered: rendered on demand in {OnDemandMs:F0} ms.");
        if (PreRendered.Count > 0)
            text += string.Create(c, $" Pre-rendered {PreRendered.Count} in {PreRendered.Sum(s => s.TotalMs):F0} ms.");
        return text;
    }
}

/// <summary>
/// Steps through slides on a <see cref="SlideOutput"/>, keeping the previous, current and next
/// slides pre-rendered (ARCH 7.2) and measuring each transition.
/// </summary>
public sealed class Presenter(SlideOutput output, IReadOnlyList<SlideSpec> slides, PreRenderer renderer, FrameMonitor frames)
{
    private readonly Dictionary<int, RenderedSlide> window = [];
    private bool busy;

    public int Index { get; private set; } = -1;
    public int Count => slides.Count;
    public PreRenderer Renderer { get; set; } = renderer;

    /// <summary>
    /// Shows slide <paramref name="index"/> (Cut when <paramref name="fadeMs"/> is 0), then
    /// pre-renders its neighbours. Null if a transition is still in progress or the index is out of range.
    /// </summary>
    public async Task<TransitionResult?> GoTo(int index, int fadeMs)
    {
        if (busy || index < 0 || index >= slides.Count)
            return null;
        busy = true;
        frames.Begin();
        try
        {
            var input = frames.Now;
            var onDemandMs = 0.0;
            if (!window.TryGetValue(index, out var slide))
            {
                slide = await Renderer.Render(slides[index]);
                window[index] = slide;
                onDemandMs = frames.Now - input;
            }

            var start = frames.Now;
            var shown = output.Show(slide, fadeMs);
            Index = index;
            var firstFrames = frames.AfterFrames(2);
            var preRender = PreRenderAround(index);

            // Rendering fires just before a frame is drawn: the second one after the change
            // is the frame after the one that contains it, an upper bound for "on screen".
            var inputToFrame = await firstFrames - input;
            await shown;
            var end = frames.Now;
            var preRendered = await preRender;
            return new TransitionResult(fadeMs, frames.Stats(start, end), inputToFrame, onDemandMs, preRendered);
        }
        finally
        {
            frames.End();
            busy = false;
        }
    }

    /// <summary>Renders the next and previous slides if missing and drops everything else.</summary>
    private async Task<IReadOnlyList<RenderTiming>> PreRenderAround(int index)
    {
        foreach (var i in window.Keys.Where(i => Math.Abs(i - index) > 1).ToList())
            window.Remove(i);

        var rendered = new List<RenderTiming>();
        foreach (var i in new[] { index + 1, index - 1 })
        {
            if (i < 0 || i >= slides.Count || window.ContainsKey(i))
                continue;
            var slide = await Renderer.Render(slides[i]);
            window[i] = slide;
            rendered.Add(slide.Timing);
        }
        return rendered;
    }
}
