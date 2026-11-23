using System.Collections.Immutable;
using ChurchSlides.Domain.Model;

namespace ChurchSlides.Domain.Editing;

/// <summary>
/// Mints new IDs for Slides and Service Items and rewrites everything that points at them
/// (Cues, Actions), so a copy never shares IDs with its original. IDs that weren't registered are left alone,
/// which keeps references to things outside the copy (other Shows, Templates, Categories) valid.
/// </summary>
internal sealed class IdRemapper
{
    private readonly Dictionary<Id<Slide>, Id<Slide>> _slides = [];
    private readonly Dictionary<Id<ServiceItem>, Id<ServiceItem>> _items = [];

    public void RegisterSlide(Id<Slide> old) => _slides[old] = Id<Slide>.New();

    public void RegisterItem(Id<ServiceItem> old) => _items[old] = Id<ServiceItem>.New();

    public Id<Slide> Map(Id<Slide> id) => _slides.GetValueOrDefault(id, id);

    public Id<ServiceItem> Map(Id<ServiceItem> id) => _items.GetValueOrDefault(id, id);

    public Cue Map(Cue cue) => cue with { SlideId = Map(cue.SlideId) };

    public ImmutableArray<TriggeredAction> Map(ImmutableArray<TriggeredAction> actions) =>
        [.. actions.Select(a => a with { Commands = [.. a.Commands.Select(Map)] })];

    private Command Map(Command command) => command switch
    {
        GoToSlide go => go with
        {
            Target = new SlideTarget(Map(go.Target.SlideId), go.Target.ItemId is { } item ? Map(item) : null),
        },
        GoToItem go => go with { ItemId = Map(go.ItemId) },
        _ => command,
    };
}
