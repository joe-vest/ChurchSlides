using System.Collections.Immutable;
using ChurchSlides.Domain;
using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Text;
using ChurchSlides.Storage.Watching;

namespace ChurchSlides.Storage.Search;

/// <summary>
/// The in-memory Catalog index (CAT-003..005). Searching reads an immutable snapshot, so it never waits for an update
/// running on another thread (decision 12). A search is a linear scan (ARCH 9.2).
/// </summary>
public sealed class CatalogSearch : ICatalogSearch
{
    private sealed record Entry(SearchHit Hit, string Title, string Ccli, string SlideText);

    private readonly object _gate = new();
    private volatile ImmutableDictionary<Id<Show>, Entry> _entries = ImmutableDictionary<Id<Show>, Entry>.Empty;

    /// <summary>Replaces the whole index.</summary>
    public void Load(IEnumerable<Show> shows)
    {
        var entries = shows.Select(ToEntry).ToImmutableDictionary(e => e.Hit.ShowId);
        lock (_gate) _entries = entries;
    }

    /// <summary>Startup: indexes every Show that loaded and is named after its own Id (LIB-009).</summary>
    public void Load(ILibrary library) =>
        Load(library.Shows.LoadAll().Where(f => f.NameMatchesId).Select(f => f.Result.Value).OfType<Show>());

    public void Upsert(Show show) => Update(entries => entries.SetItem(show.Id, ToEntry(show)));

    public void Remove(Id<Show> id) => Update(entries => entries.Remove(id));

    /// <summary>Reloads the Shows a monitor update says changed and drops the removed ones. A Show that no longer loads is dropped too.</summary>
    public void Apply(LibraryUpdate update, ILibrary library)
    {
        var shows = update.Changes.Where(c => c is { Kind: LibraryFileKind.Show, Id: not null }).ToList();
        if (shows.Count == 0) return;

        Update(entries =>
        {
            foreach (var change in shows)
            {
                var id = new Id<Show>(change.Id!.Value);
                var loaded = change.Type == ChangeType.Removed ? null : library.Shows.Load(id);
                entries = loaded is { NameMatchesId: true, Result.Value: { } show }
                    ? entries.SetItem(id, ToEntry(show))
                    : entries.Remove(id);
            }
            return entries;
        });
    }

    public IReadOnlyList<SearchHit> Search(SearchQuery query)
    {
        var text = SearchText.Normalize(query.Text);
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return [.. _entries.Values
            .Where(e => (query.Kind is null || e.Hit.Kind == query.Kind) && (query.CategoryId is null || e.Hit.CategoryId == query.CategoryId))
            .Select(e => MatchOf(e, text, words) is { } match ? e.Hit with { Match = match } : null)
            .OfType<SearchHit>()
            .OrderBy(h => h.Match)
            .ThenBy(h => h.Title, StringComparer.OrdinalIgnoreCase)];
    }

    private static SearchMatch? MatchOf(Entry e, string text, string[] words)
    {
        if (words.Length == 0) return SearchMatch.All;
        if (e.Title.StartsWith(text, StringComparison.Ordinal)) return SearchMatch.TitleStart;
        if (ContainsAll(e.Title, words)) return SearchMatch.Title;
        if (e.Ccli == text) return SearchMatch.CcliNumber;
        if (ContainsAll(e.SlideText, words)) return SearchMatch.SlideText;
        return null;
    }

    private static bool ContainsAll(string field, string[] words) =>
        words.All(w => field.Contains(w, StringComparison.Ordinal));

    private void Update(Func<ImmutableDictionary<Id<Show>, Entry>, ImmutableDictionary<Id<Show>, Entry>> change)
    {
        lock (_gate) _entries = change(_entries);
    }

    private static Entry ToEntry(Show show) => new(
        new SearchHit(show.Id, show.Title, show.Kind, show.CategoryId, SearchMatch.All),
        SearchText.Normalize(show.Title),
        SearchText.Normalize(show.Metadata?.CcliSongNumber),
        SearchText.Normalize(string.Join(' ', show.Slides
            .SelectMany(s => s.TextBoxes)
            .SelectMany(b => b.Content.Paragraphs)
            .Select(p => string.Concat(p.Inlines.OfType<TextRun>().Select(r => r.Text))))));
}
