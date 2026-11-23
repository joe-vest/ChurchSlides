namespace ChurchSlides.Domain.Model;

/// <summary>The Category every Library has and that can't be deleted (CAT-010). Scripture generation fills it (SCR-013).</summary>
public static class BuiltInCategories
{
    /// <summary>The same on every machine, so a synced Library never holds two.</summary>
    public static Id<Category> ScriptureId { get; } = new(Guid.Parse("793b069a-9c4a-482c-9b09-f94a9b196401"));

    public static Category Scripture { get; } = new() { Id = ScriptureId, Name = "Scripture" };

    public static bool IsPresentIn(CategoryList list) => list.Categories.Any(c => c.Id == ScriptureId);

    /// <summary>Adds Scripture if it's missing. A list that has it (even renamed) is returned unchanged.</summary>
    public static CategoryList EnsureIn(CategoryList list) =>
        IsPresentIn(list) ? list : list with { Categories = list.Categories.Add(Scripture) };
}
