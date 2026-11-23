using ChurchSlides.Domain.Model;

namespace ChurchSlides.Domain.Resolution;

/// <summary>An immutable snapshot of the Templates and Categories needed to resolve slides.</summary>
public sealed class TemplateSet
{
    public static TemplateSet Empty { get; } = new([], [], null);

    public TemplateSet(IEnumerable<Template> templates, IEnumerable<Category> categories, Id<Template>? libraryDefault)
    {
        var t = new Dictionary<Id<Template>, Template>();
        foreach (var template in templates) t[template.Id] = template;
        var c = new Dictionary<Id<Category>, Category>();
        foreach (var category in categories) c[category.Id] = category;

        Templates = t;
        Categories = c;
        LibraryDefault = libraryDefault;
    }

    public IReadOnlyDictionary<Id<Template>, Template> Templates { get; }
    public IReadOnlyDictionary<Id<Category>, Category> Categories { get; }
    public Id<Template>? LibraryDefault { get; }
}

/// <summary>Where a slide sits among the slides actually shown for its Show (skipped slides don't count).</summary>
public readonly record struct SlidePosition(bool IsFirst, bool IsLast)
{
    public static SlidePosition For(Show show, Slide slide, IReadOnlyCollection<Id<Slide>> skipped)
    {
        var shown = show.Slides.Where(s => !skipped.Contains(s.Id)).ToList();
        var index = shown.FindIndex(s => s.Id == slide.Id);
        return index < 0 ? default : new SlidePosition(index == 0, index == shown.Count - 1);
    }
}

/// <summary>Everything outside the slide itself that resolution needs.</summary>
public sealed record ResolutionContext
{
    public required TemplateSet Templates { get; init; }
    public required LibrarySettings Library { get; init; }
    public required Show Show { get; init; }

    /// <summary>The Service being presented. Null when a Show is viewed outside a Service (VAR-003).</summary>
    public Service? Service { get; init; }

    public SlidePosition Position { get; init; }
}
