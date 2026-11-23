using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ChurchSlides.Domain.Model;

public enum ShowKind { Song, Presentation }

public enum CopyrightPlacement { None, FirstSlide, LastSlide }

/// <summary>A reusable catalog item: an ordered, flat list of Slides plus metadata (DEC-002).</summary>
public sealed record Show
{
    [JsonRequired] public Id<Show> Id { get; init; }
    [JsonRequired] public string Title { get; init; } = "";
    [JsonRequired] public ShowKind Kind { get; init; }
    [JsonRequired] public Id<Category> CategoryId { get; init; }
    [JsonRequired] public CopyrightPlacement CopyrightPlacement { get; init; }

    [JsonRequired] public ImmutableArray<string> Tags { get; init; } = [];
    public SongMetadata? Metadata { get; init; }
    public Id<Template>? DefaultTemplateId { get; init; }
    [JsonRequired] public ImmutableArray<Slide> Slides { get; init; } = [];
    public AudioTrack? Audio { get; init; }
    [JsonRequired] public ImmutableArray<TriggeredAction> Actions { get; init; } = [];

    /// <summary>SHOW-004 (assumption): Songs show the notice on the first slide, Presentations don't show one.</summary>
    public static CopyrightPlacement DefaultCopyrightPlacement(ShowKind kind) =>
        kind == ShowKind.Song ? CopyrightPlacement.FirstSlide : CopyrightPlacement.None;
}

public sealed record SongMetadata
{
    public string? Authors { get; init; }
    public string? Copyright { get; init; }
    public string? CcliSongNumber { get; init; }
    [JsonRequired] public bool IsPublicDomain { get; init; }
}

/// <summary>The smallest displayable unit. The label is for operators and is never projected (SHOW-002).</summary>
public sealed record Slide
{
    [JsonRequired] public Id<Slide> Id { get; init; }
    [JsonRequired] public string Label { get; init; } = "";
    public Id<Template>? TemplateOverrideId { get; init; }
    public Background? BackgroundOverride { get; init; }
    [JsonRequired] public ImmutableArray<SlideTextBox> TextBoxes { get; init; } = [];
    [JsonRequired] public ImmutableArray<TriggeredAction> Actions { get; init; } = [];
}

/// <summary>An audio file attached to a Show (AUD-001). Cue positions are absolute file time (AUD-005).</summary>
public sealed record AudioTrack
{
    [JsonRequired] public LibraryRelativePath MediaPath { get; init; }
    public TimeSpan? TrimStart { get; init; }
    public TimeSpan? TrimEnd { get; init; }
    [JsonRequired] public ImmutableArray<Cue> Cues { get; init; } = [];
}

/// <summary>When the audio reaches Position, this slide must be displayed (AUD-004, AUD-006).</summary>
public sealed record Cue(Id<Slide> SlideId, TimeSpan Position);
