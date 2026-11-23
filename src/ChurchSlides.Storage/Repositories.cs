using ChurchSlides.Domain;
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

internal sealed class SingletonRepository<T>(
    string path,
    Func<T, IReadOnlyList<string>> validate,
    LibraryFileStore files) : ISingletonRepository<T> where T : class, new()
{
    public LoadedFile<T> Load() =>
        files.Load<T>(path)
        ?? new(new(LoadStatus.Ok, SchemaVersions.Current<T>(), new T(), null), null, path, true);

    public SaveResult Save(T value, FileStamp? expected)
    {
        var problems = validate(value);
        if (problems.Count > 0) return new(SaveOutcome.Invalid, Problems: problems);
        return files.Save(path, value, expected);
    }
}
