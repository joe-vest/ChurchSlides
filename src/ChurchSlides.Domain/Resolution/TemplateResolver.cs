using System.Collections.Immutable;
using ChurchSlides.Domain.Model;

namespace ChurchSlides.Domain.Resolution;

public sealed record TemplateResolution(Template Template, ImmutableArray<RenderProblem> Problems);

public static class TemplateResolver
{
    /// <summary>Plain black Template used when nothing else resolves, so the Output always renders something.</summary>
    public static Template Fallback { get; } = new()
    {
        Id = new Id<Template>(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")),
        Name = "Built-in",
        Background = new Background { Kind = MediaKind.SolidColor, ColorHex = "#000000" },
        Boxes =
        [
            new TemplateBox { Name = "body", Geometry = new RectRelative(0.05, 0.05, 0.9, 0.8) },
            new TemplateBox
            {
                Name = "copyright",
                Geometry = new RectRelative(0.05, 0.9, 0.9, 0.07),
                Defaults = new TextDefaults { FontSizeRelative = 0.025, VAlign = VerticalAlign.Bottom },
                IsCopyrightBox = true,
            },
        ],
    };

    /// <summary>
    /// TPL-010: Slide override, then Show default, then Category default, then Library default.
    /// An ID whose Template no longer exists is skipped and reported (TPL-002).
    /// </summary>
    public static TemplateResolution Resolve(Slide slide, Show show, TemplateSet set)
    {
        var problems = ImmutableArray.CreateBuilder<RenderProblem>();

        foreach (var id in Candidates(slide, show, set))
        {
            if (set.Templates.TryGetValue(id, out var template))
                return new TemplateResolution(template, problems.ToImmutable());
            problems.Add(new TemplateMissing(id));
        }

        problems.Add(new NoTemplateAvailable());
        return new TemplateResolution(Fallback, problems.ToImmutable());
    }

    private static IEnumerable<Id<Template>> Candidates(Slide slide, Show show, TemplateSet set)
    {
        if (slide.TemplateOverrideId is { } slideId) yield return slideId;
        if (show.DefaultTemplateId is { } showId) yield return showId;
        if (set.Categories.TryGetValue(show.CategoryId, out var category) && category.DefaultTemplateId is { } categoryId)
            yield return categoryId;
        if (set.LibraryDefault is { } libraryId) yield return libraryId;
    }
}

/// <summary>Lookups of boxes inside a Template.</summary>
public static class TemplateBoxes
{
    /// <summary>Where text goes when a Template has no content box to offer.</summary>
    public static RectRelative FullCanvas { get; } = new(0.05, 0.05, 0.9, 0.9);

    /// <summary>Finds a content box by name, ignoring case. The copyright box is never matched (TPL-001, CCLI-004).</summary>
    public static TemplateBox? Find(Template template, string name) =>
        template.Boxes.FirstOrDefault(b => !b.IsCopyrightBox && string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The first box that isn't the copyright box: where unplaced or mismatched text goes (TPL-027).</summary>
    public static TemplateBox? FirstContent(Template template) =>
        template.Boxes.FirstOrDefault(b => !b.IsCopyrightBox);
}
