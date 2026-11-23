using ChurchSlides.Domain;
using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Serialization;

namespace ChurchSlides.Storage;

/// <summary>One file per object, named <c>{id}.json</c>, in a folder per kind (LIB-002).</summary>
public interface IObjectRepository<T> where T : class
{
    /// <summary>Null when there is no file for this Id.</summary>
    LoadedFile<T>? Load(Id<T> id);

    /// <summary>Every <c>.json</c> file in the folder, including unreadable ones (with their error). Ignores <c>.saving</c> files. An absent folder gives an empty list.</summary>
    IReadOnlyList<LoadedFile<T>> LoadAll();

    /// <param name="expected">The stamp from when the object was loaded. Null for a new object.</param>
    SaveResult Save(T value, FileStamp? expected);

    SaveResult Delete(Id<T> id, FileStamp? expected);
}

/// <summary>A single named file (categories.json, library.json).</summary>
public interface ISingletonRepository<T> where T : class
{
    /// <summary>A missing file gives a default object and a null stamp.</summary>
    LoadedFile<T> Load();

    SaveResult Save(T value, FileStamp? expected);
}

internal sealed class ObjectRepository<T>(
    string folder,
    Func<T, Id<T>> idOf,
    Func<T, IReadOnlyList<string>> validate,
    LibraryFileStore files) : IObjectRepository<T> where T : class
{
    private const string Extension = ".json";

    private string PathFor(Id<T> id) => Path.Combine(folder, id + Extension);

    public LoadedFile<T>? Load(Id<T> id) => files.Load<T>(PathFor(id), v => idOf(v).Value);

    public IReadOnlyList<LoadedFile<T>> LoadAll()
    {
        if (!Directory.Exists(folder)) return [];

        var paths = Directory.EnumerateFiles(folder)
            .Where(p => string.Equals(Path.GetExtension(p), Extension, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase);
        return [.. paths.Select(p => files.Load<T>(p, v => idOf(v).Value)).OfType<LoadedFile<T>>()];
    }

    public SaveResult Save(T value, FileStamp? expected)
    {
        var problems = validate(value);
        if (problems.Count > 0) return new(SaveOutcome.Invalid, Problems: problems);
        return files.Save(PathFor(idOf(value)), value, expected);
    }

    public SaveResult Delete(Id<T> id, FileStamp? expected) => files.Delete<T>(PathFor(id), expected);
}

/// <summary>What <see cref="IServiceStateRepository.Load"/> found. State is always usable: the defaults when the file is missing or damaged.</summary>
public sealed record ServiceStateLoad(ServiceState State, LoadStatus Status, string? Error);

/// <summary>One state file per Service, <c>service-state/{service id}.json</c> (LIB-013). The last write wins.</summary>
public interface IServiceStateRepository
{
    /// <summary>Missing gives the defaults with status Ok. Damaged gives the defaults with status Unreadable, and the next save repairs it.</summary>
    ServiceStateLoad Load(Id<Service> serviceId);

    SaveResult Save(ServiceState state);

    SaveResult Delete(Id<Service> serviceId);
}

internal sealed class ServiceStateRepository(string folder, LibraryFileStore files) : IServiceStateRepository
{
    private string PathFor(Id<Service> id) => Path.Combine(folder, id + ".json");

    public ServiceStateLoad Load(Id<Service> serviceId)
    {
        var defaults = ServiceState.Defaults(serviceId);
        if (files.Load<ServiceState>(PathFor(serviceId)) is not { } loaded)
            return new(defaults, LoadStatus.Ok, null);

        var result = loaded.Result;
        return new(result.Value ?? defaults, result.Status, result.Error);
    }

    public SaveResult Save(ServiceState state)
    {
        var problems = SaveValidation.Validate(state);
        if (problems.Count > 0) return new(SaveOutcome.Invalid, Problems: problems);
        return files.Save(PathFor(state.ServiceId), state, expected: null, overwrite: true);
    }

    public SaveResult Delete(Id<Service> serviceId) =>
        files.Load<ServiceState>(PathFor(serviceId)) is { } loaded
            ? files.Delete<ServiceState>(loaded.Path, loaded.Stamp)
            : new(SaveOutcome.Unchanged);
}

internal sealed class SingletonRepository<T>(
    string path,
    Func<T, IReadOnlyList<string>> validate,
    LibraryFileStore files,
    Func<T, T>? afterLoad = null) : ISingletonRepository<T> where T : class, new()
{
    public LoadedFile<T> Load()
    {
        var loaded = files.Load<T>(path)
            ?? new(new(LoadStatus.Ok, SchemaVersions.Current<T>(), new T(), null), null, path, true);
        if (afterLoad is null || loaded.Result.Value is not { } value) return loaded;
        return loaded with { Result = loaded.Result with { Value = afterLoad(value) } };
    }

    public SaveResult Save(T value, FileStamp? expected)
    {
        var problems = validate(value);
        if (problems.Count > 0) return new(SaveOutcome.Invalid, Problems: problems);
        return files.Save(path, value, expected);
    }
}
