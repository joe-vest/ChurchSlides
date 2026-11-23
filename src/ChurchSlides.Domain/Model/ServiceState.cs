using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ChurchSlides.Domain.Model;

/// <summary>A pre-service check the operator dismissed. Rule and target are plain text keys (CHK-002).</summary>
public sealed record CheckDismissal
{
    [JsonRequired] public string Rule { get; init; } = "";
    public string? Target { get; init; }
}

/// <summary>
/// What belongs to one Service but isn't the run order: its Mode, dismissed checks and the answer to
/// "Log this as a live service?" (LIB-013). Stored in service-state/{service id}.json. No file means defaults.
/// </summary>
public sealed record ServiceState
{
    [JsonRequired] public Id<Service> ServiceId { get; init; }
    [JsonRequired] public ServiceMode Mode { get; init; } = ServiceMode.Rehearse;
    [JsonRequired] public ImmutableArray<CheckDismissal> Dismissals { get; init; } = [];

    /// <summary>True once the user answered No to "Log this as a live service?" (CCLI-016).</summary>
    [JsonRequired] public bool LogPromptDeclined { get; init; }

    public static ServiceState Defaults(Id<Service> serviceId) => new() { ServiceId = serviceId };
}
