using System.Text.Json.Serialization;
using System.Collections.Immutable;

namespace ChurchSlides.Domain.Model;

/// <summary>A flat label. Each Show has exactly one (CAT-002). May carry a default Template (TPL-012).</summary>
public sealed record Category
{
    [JsonRequired] public Id<Category> Id { get; init; }
    [JsonRequired] public string Name { get; init; } = "";
    public Id<Template>? DefaultTemplateId { get; init; }
}

/// <summary>The contents of categories.json.</summary>
public sealed record CategoryList
{
    [JsonRequired] public ImmutableArray<Category> Categories { get; init; } = [];
}

public enum CanvasAspect { Ratio16x10, Ratio16x9, Ratio4x3 }

public enum TransitionKind { Cut, Crossfade }

/// <summary>Library-wide settings that sync between machines (library.json). Machine-local settings live elsewhere (LIB-008).</summary>
public sealed record LibrarySettings
{
    public const string DefaultCopyrightFormat =
        "{{show.title}} — {{show.authors}} · © {{show.copyright}} · Used by permission · CCLI License #{{church.ccliLicense}}";

    [JsonRequired] public CanvasAspect Canvas { get; init; } = CanvasAspect.Ratio16x10;
    public string? CcliLicenseNumber { get; init; }
    public LibraryRelativePath? LogoPath { get; init; }
    [JsonRequired] public TransitionKind Transition { get; init; } = TransitionKind.Crossfade;
    [JsonRequired] public int CrossfadeMilliseconds { get; init; } = 300;
    [JsonRequired] public string CopyrightFormat { get; init; } = DefaultCopyrightFormat;
    public Id<Template>? DefaultTemplateId { get; init; }
    public Id<Template>? DefaultScriptureTemplateId { get; init; }
    public DateOnly? CcliReportingPeriodStart { get; init; }
    public DateOnly? CcliReportingPeriodEnd { get; init; }
}
