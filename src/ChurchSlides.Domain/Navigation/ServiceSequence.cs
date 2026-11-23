using System.Collections.Immutable;
using ChurchSlides.Domain.Editing;
using ChurchSlides.Domain.Model;

namespace ChurchSlides.Domain.Navigation;

/// <summary>A position in a Service: always an item plus a slide (LIVE-016, ARCH 5.2).</summary>
public readonly record struct SlideRef(Id<ServiceItem> ItemId, Id<Slide> SlideId);

/// <summary>Why an item has the slides it has (LIVE-006, SVC-026, SVC-034).</summary>
public enum SequenceItemStatus
{
    /// <summary>A Show is known: a reference whose Show exists, a Show Copy, or a generated Scripture Item.</summary>
    Show,

    /// <summary>The referenced Show isn't in the Catalog (SVC-026).</summary>
    MissingShow,

    /// <summary>An unfilled Placeholder (SVC-034).</summary>
    UnfilledPlaceholder,

    /// <summary>A Scripture Item whose slides haven't been generated yet.</summary>
    NotGenerated,
}

/// <summary>One slide of an item. A Skipped slide is listed but passed over by Next and Previous (LIVE-017).</summary>
public sealed record SequenceSlide(Id<Slide> SlideId, bool IsSkipped)
{
    public bool IsDisplayable => !IsSkipped;
}

/// <summary>One Service Item with every slide of its Show. <see cref="Show"/> is null unless the status is Show.</summary>
public sealed record SequenceItem(
    Id<ServiceItem> ItemId,
    SequenceItemStatus Status,
    Show? Show,
    ImmutableArray<SequenceSlide> Slides)
{
    /// <summary>False for items that navigation passes over, and whose header click does nothing (LIVE-006).</summary>
    public bool HasDisplayable => Slides.Any(s => s.IsDisplayable);
}

/// <summary>
/// Every item and slide of a Service, two levels as the operator sees them (ARCH 5.2), with the searches navigation
/// needs. Immutable: rebuild it whenever the Service or a Show changes. Next and Previous cross item boundaries
/// (LIVE-005) and stop at the ends (LIVE-007).
/// </summary>
public sealed class ServiceSequence
{
    private readonly ImmutableArray<Entry> _flat;
    private readonly Dictionary<SlideRef, int> _index;

    private readonly record struct Entry(SlideRef Ref, SequenceSlide Slide);

    private ServiceSequence(ImmutableArray<SequenceItem> items)
    {
        Items = items;
        _flat = [.. items.SelectMany(i => i.Slides.Select(s => new Entry(new SlideRef(i.ItemId, s.SlideId), s)))];
        _index = _flat.Select((e, n) => (e.Ref, n)).ToDictionary(x => x.Ref, x => x.n);
        All = [.. _flat.Select(e => e.Ref)];
    }

    public ImmutableArray<SequenceItem> Items { get; }

    /// <summary>Every slide in play order, skipped ones included. For finding a removed slide's neighbours (LIVE-041).</summary>
    public ImmutableArray<SlideRef> All { get; }

    /// <summary>
    /// Builds the sequence. <paramref name="findShow"/> looks up catalog Shows and returns null for a missing one.
    /// The skip list in force comes from <see cref="SkipRules.InForce"/>, so cued Shows skip nothing (SVC-025).
    /// </summary>
    public static ServiceSequence Build(Service service, Func<Id<Show>, Show?> findShow) =>
        new([.. service.Items.Select(item => ItemOf(item, findShow))]);

    private static SequenceItem ItemOf(ServiceItem item, Func<Id<Show>, Show?> findShow)
    {
        var (status, show) = item switch
        {
            ShowReferenceItem r => findShow(r.ShowId) is { } s ? (SequenceItemStatus.Show, s) : (SequenceItemStatus.MissingShow, null),
            ShowCopyItem c => (SequenceItemStatus.Show, c.LocalShow),
            ScriptureItem { Generated: { } g } => (SequenceItemStatus.Show, g),
            ScriptureItem => (SequenceItemStatus.NotGenerated, null),
            PlaceholderItem => (SequenceItemStatus.UnfilledPlaceholder, (Show?)null),
            _ => throw new InvalidOperationException($"Unknown Service Item type {item.GetType().Name}."),
        };
        if (show is null) return new(item.Id, status, null, []);

        var skipped = item is ShowItem showItem ? SkipRules.InForce(showItem, show) : [];
        return new(item.Id, status, show, [.. show.Slides.Select(s => new SequenceSlide(s.Id, skipped.Contains(s.Id)))]);
    }

    public bool Contains(SlideRef slide) => _index.ContainsKey(slide);

    public SequenceSlide? Find(SlideRef slide) => _index.TryGetValue(slide, out var n) ? _flat[n].Slide : null;

    /// <summary>The first displayable slide of the Service, or null if nothing can be shown.</summary>
    public SlideRef? FirstDisplayable() => Scan(0, +1);

    /// <summary>The nearest displayable slide after <paramref name="from"/>, which may itself be skipped (LIVE-017).</summary>
    public SlideRef? NextDisplayable(SlideRef from) => _index.TryGetValue(from, out var n) ? Scan(n + 1, +1) : null;

    /// <summary>The nearest displayable slide before <paramref name="from"/>.</summary>
    public SlideRef? PreviousDisplayable(SlideRef from) => _index.TryGetValue(from, out var n) ? Scan(n - 1, -1) : null;

    /// <summary>The first displayable slide of an item (a header click), or null if it has none (LIVE-006).</summary>
    public SlideRef? FirstDisplayableOf(Id<ServiceItem> itemId) => ItemIndex(itemId) is var n and >= 0 ? FirstOf(Items[n]) : null;

    /// <summary>Next item: the first displayable slide of the nearest later item that has one (LIVE-016).</summary>
    public SlideRef? NextItemWithDisplayable(Id<ServiceItem> itemId) => ItemScan(itemId, +1);

    /// <summary>Previous item: the first displayable slide of the nearest earlier item that has one (LIVE-016).</summary>
    public SlideRef? PreviousItemWithDisplayable(Id<ServiceItem> itemId) => ItemScan(itemId, -1);

    private SlideRef? Scan(int start, int step)
    {
        for (var n = start; n >= 0 && n < _flat.Length; n += step)
        {
            if (_flat[n].Slide.IsDisplayable) return _flat[n].Ref;
        }

        return null;
    }

    private SlideRef? ItemScan(Id<ServiceItem> itemId, int step)
    {
        var at = ItemIndex(itemId);
        if (at < 0) return null;

        for (var n = at + step; n >= 0 && n < Items.Length; n += step)
        {
            if (FirstOf(Items[n]) is { } first) return first;
        }

        return null;
    }

    private int ItemIndex(Id<ServiceItem> itemId)
    {
        for (var n = 0; n < Items.Length; n++)
        {
            if (Items[n].ItemId == itemId) return n;
        }

        return -1;
    }

    private static SlideRef? FirstOf(SequenceItem item) =>
        item.Slides.FirstOrDefault(s => s.IsDisplayable) is { } s ? new SlideRef(item.ItemId, s.SlideId) : null;
}
