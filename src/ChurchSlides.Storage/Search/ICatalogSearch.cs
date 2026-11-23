using ChurchSlides.Domain;
using ChurchSlides.Domain.Model;

namespace ChurchSlides.Storage.Search;

/// <summary>An empty <see cref="Text"/> lists every Show. Kind and Category narrow the result (CAT-003).</summary>
public sealed record SearchQuery(string Text, ShowKind? Kind = null, Id<Category>? CategoryId = null);

/// <summary>Best match first (CAT-004). <see cref="All"/> is the empty query.</summary>
public enum SearchMatch { All, TitleStart, Title, CcliNumber, SlideText }

public sealed record SearchHit(Id<Show> ShowId, string Title, ShowKind Kind, Id<Category> CategoryId, SearchMatch Match);

public interface ICatalogSearch
{
    IReadOnlyList<SearchHit> Search(SearchQuery query);
}
