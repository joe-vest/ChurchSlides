using ChurchSlides.Domain.Model;

namespace ChurchSlides.Domain.Editing;

/// <summary>Forking, reverting and duplicating Shows (DEC-005, DEC-006, SVC-022, SVC-024).</summary>
public static class ShowCopying
{
    /// <summary>
    /// Turns a reference into a Service-local copy. The Service Item ID and every Slide and Text Box ID stay the same,
    /// so skips, Cues and Actions keep working. Only the local Show gets a new Show ID.
    /// </summary>
    public static ShowCopyItem Fork(ShowReferenceItem reference, Show catalogShow)
    {
        if (reference.ShowId != catalogShow.Id)
            throw new ArgumentException("The reference does not point at this Show.", nameof(catalogShow));

        return new ShowCopyItem
        {
            Id = reference.Id,
            TemplateOverrideId = reference.TemplateOverrideId,
            Actions = reference.Actions,
            SkippedSlideIds = reference.SkippedSlideIds,
            FilledFrom = reference.FilledFrom,
            LocalShow = catalogShow with { Id = Id<Show>.New() },
            OriginShowId = catalogShow.Id,
        };
    }

    public static bool CanRevert(ShowCopyItem copy) => copy.OriginShowId is not null;

    /// <summary>
    /// Discards the copy and points back at the catalog Show. Skips for slides that no longer exist in the catalog
    /// Show are dropped. Throws if the copy has no origin or <paramref name="catalogShow"/> isn't it.
    /// </summary>
    public static ShowReferenceItem Revert(ShowCopyItem copy, Show catalogShow)
    {
        if (copy.OriginShowId != catalogShow.Id)
            throw new InvalidOperationException("This copy was not made from that Show, so it can't be reverted to it.");

        var existing = catalogShow.Slides.Select(s => s.Id).ToHashSet();
        return new ShowReferenceItem
        {
            Id = copy.Id,
            TemplateOverrideId = copy.TemplateOverrideId,
            Actions = copy.Actions,
            SkippedSlideIds = [.. copy.SkippedSlideIds.Where(existing.Contains)],
            FilledFrom = copy.FilledFrom,
            ShowId = catalogShow.Id,
        };
    }

    /// <summary>
    /// An independent copy of a Show (DEC-005): new Show, Slide and Text Box IDs, with Cues and Actions that point at
    /// the Show's own slides rewritten. Category and Template references are untouched.
    /// </summary>
    public static Show Duplicate(Show show)
    {
        var ids = new IdRemapper();
        foreach (var slide in show.Slides) ids.RegisterSlide(slide.Id);

        return show with
        {
            Id = Id<Show>.New(),
            Slides =
            [
                .. show.Slides.Select(slide => slide with
                {
                    Id = ids.Map(slide.Id),
                    TextBoxes = [.. slide.TextBoxes.Select(box => box with { Id = Id<SlideTextBox>.New() })],
                    Actions = ids.Map(slide.Actions),
                }),
            ],
            Audio = show.Audio is { } audio ? audio with { Cues = [.. audio.Cues.Select(ids.Map)] } : null,
            Actions = ids.Map(show.Actions),
        };
    }
}
