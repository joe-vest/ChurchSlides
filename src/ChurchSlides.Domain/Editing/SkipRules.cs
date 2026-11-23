using System.Collections.Immutable;
using ChurchSlides.Domain.Model;

namespace ChurchSlides.Domain.Editing;

/// <summary>The outcome of a change that may be refused. When refused, <see cref="Value"/> is the unchanged input.</summary>
public sealed record SkipResult<T>(T Value, string? RefusedReason = null)
{
    public bool Succeeded => RefusedReason is null;
}

/// <summary>Skipping slides in a Service (SVC-021, SVC-025) and marking them Skipped in the catalog (SHOW-008, SVC-027).</summary>
public static class SkipRules
{
    public const string CuedAudioReason =
        "Skipping isn't available for a song with audio cues, because the cues need every slide.";

    public const string UnknownSlideReason = "That slide isn't in this Show.";

    /// <summary>SVC-025: a Show whose audio has Cues needs the full sequence of slides.</summary>
    public static bool CanSkip(Show show) => show.Audio is not { Cues.Length: > 0 };

    /// <summary>
    /// Skips or un-skips one slide in a Service Item. Un-skipping is always allowed, including slides the catalog
    /// marks Skipped. Changes apply only to this item.
    /// </summary>
    public static SkipResult<ShowItem> SetSkipped(ShowItem item, Show show, Id<Slide> slideId, bool skipped)
    {
        if (show.Slides.All(s => s.Id != slideId)) return new(item, UnknownSlideReason);

        var isSkipped = item.SkippedSlideIds.Contains(slideId);
        if (skipped == isSkipped) return new(item);
        if (skipped && !CanSkip(show)) return new(item, CuedAudioReason);

        return new(item.WithSkippedSlides(skipped
            ? item.SkippedSlideIds.Add(slideId)
            : item.SkippedSlideIds.Remove(slideId)));
    }

    /// <summary>Marks or un-marks a slide as Skipped by default in the catalog (SHOW-008).</summary>
    public static SkipResult<Show> SetSkippedByDefault(Show show, Id<Slide> slideId, bool skipped)
    {
        var slide = show.Slides.FirstOrDefault(s => s.Id == slideId);
        if (slide is null) return new(show, UnknownSlideReason);
        if (slide.SkippedByDefault == skipped) return new(show);
        if (skipped && !CanSkip(show)) return new(show, CuedAudioReason);

        return new(show with
        {
            Slides = [.. show.Slides.Select(s => s.Id == slideId ? s with { SkippedByDefault = skipped } : s)],
        });
    }

    /// <summary>The slides a new Service Item should start with skipped (SVC-027). Empty for Shows that can't skip.</summary>
    public static ImmutableArray<Id<Slide>> DefaultsFor(Show show) =>
        CanSkip(show) ? [.. show.Slides.Where(s => s.SkippedByDefault).Select(s => s.Id)] : [];

    /// <summary>"Reset to catalog defaults" (SVC-028).</summary>
    public static ShowReferenceItem ResetToCatalog(ShowReferenceItem item, Show catalogShow) =>
        item with { SkippedSlideIds = DefaultsFor(catalogShow) };
}

/// <summary>Creating Show Reference items.</summary>
public static class ShowItems
{
    /// <summary>Adds a catalog Show to a Service. The catalog's Skipped marks are copied in (SVC-027).</summary>
    public static ShowReferenceItem AddReference(Show show) => new()
    {
        Id = Id<ServiceItem>.New(),
        ShowId = show.Id,
        SkippedSlideIds = SkipRules.DefaultsFor(show),
    };

    /// <summary>
    /// Fills a Placeholder with a Show (SVC-031). The item keeps its ID, Actions and Template override, remembers the
    /// Placeholder so it can be cleared back (SVC-033), and gets the catalog's Skipped marks (SVC-027).
    /// </summary>
    public static ShowReferenceItem FillPlaceholder(PlaceholderItem placeholder, Show show) => new()
    {
        Id = placeholder.Id,
        TemplateOverrideId = placeholder.TemplateOverrideId,
        Actions = placeholder.Actions,
        ShowId = show.Id,
        SkippedSlideIds = SkipRules.DefaultsFor(show),
        FilledFrom = placeholder.Placeholder,
    };
}

public enum SlideEditAction
{
    /// <summary>Fork into a Service-local copy without asking.</summary>
    ForkAutomatically,

    /// <summary>Ask: edit the catalog Show (the default) or make a copy for this Service only.</summary>
    PromptCatalogOrCopy,
}

public static class EditPolicy
{
    /// <summary>SVC-022: what to do when slide content of a Show Reference is edited from inside a Service.</summary>
    public static SlideEditAction ForSlideEdit(Show show) =>
        show.Kind == ShowKind.Presentation ? SlideEditAction.ForkAutomatically : SlideEditAction.PromptCatalogOrCopy;
}
