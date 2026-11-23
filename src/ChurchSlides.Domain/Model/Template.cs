using System.Text.Json.Serialization;
using System.Collections.Immutable;

namespace ChurchSlides.Domain.Model;

/// <summary>A reusable layout: a background and named boxes with default formatting (TPL-001).</summary>
public sealed record Template
{
    [JsonRequired] public Id<Template> Id { get; init; }
    [JsonRequired] public string Name { get; init; } = "";
    [JsonRequired] public Background Background { get; init; } = new();
    [JsonRequired] public ImmutableArray<TemplateBox> Boxes { get; init; } = [];
}

public sealed record TemplateBox
{
    [JsonRequired] public string Name { get; init; } = "";
    [JsonRequired] public RectRelative Geometry { get; init; } = null!;
    [JsonRequired] public TextDefaults Defaults { get; init; } = new();

    /// <summary>The box that receives the copyright notice (CCLI-004).</summary>
    [JsonRequired] public bool IsCopyrightBox { get; init; }
}
