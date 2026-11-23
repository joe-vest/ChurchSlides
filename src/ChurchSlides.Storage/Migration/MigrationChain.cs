using System.Text.Json.Nodes;

namespace ChurchSlides.Storage.Migration;

/// <summary>
/// Upgrades one file kind's JSON from version N to N+1 (LIB-004). Runs on the JSON before it is deserialized,
/// so a step can add a field that the new model requires.
/// </summary>
public interface IMigrationStep
{
    Type Kind { get; }
    int FromVersion { get; }

    /// <summary>Changes the envelope's "data" object in place.</summary>
    void Apply(JsonObject data);
}

public sealed class MigrationStep<T>(int fromVersion, Action<JsonObject> apply) : IMigrationStep
{
    public Type Kind => typeof(T);
    public int FromVersion => fromVersion;
    public void Apply(JsonObject data) => apply(data);
}

public sealed record MigrationOutcome(bool Ok, string? Error = null);

/// <summary>The registered steps. Empty today: every file kind is still at version 1.</summary>
public sealed class MigrationChain
{
    private readonly Dictionary<(Type, int), IMigrationStep> _steps = [];

    public MigrationChain(IEnumerable<IMigrationStep>? steps = null)
    {
        foreach (var step in steps ?? [])
            if (!_steps.TryAdd((step.Kind, step.FromVersion), step))
                throw new ArgumentException($"Two migration steps for {step.Kind.Name} from version {step.FromVersion}.");
    }

    /// <summary>Walks the envelope from <paramref name="fromVersion"/> up to <paramref name="toVersion"/> and stamps the new version.</summary>
    public MigrationOutcome Migrate(JsonObject envelope, Type kind, int fromVersion, int toVersion)
    {
        if (envelope["data"] is not JsonObject data)
            return new(false, "The file has no data section.");

        for (var version = fromVersion; version < toVersion; version++)
        {
            if (!_steps.TryGetValue((kind, version), out var step))
                return new(false, $"This app has no way to upgrade a {kind.Name} file from version {version} to {version + 1}.");
            step.Apply(data);
            envelope["schemaVersion"] = version + 1;
        }
        return new(true);
    }
}
