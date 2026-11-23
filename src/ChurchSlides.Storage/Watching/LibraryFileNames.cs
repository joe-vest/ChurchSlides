using System.Text.RegularExpressions;

namespace ChurchSlides.Storage.Watching;

public enum LibraryFileKind { Show, Service, Template, ServiceTemplate, ServiceState, Categories, Settings }

public enum LibraryFileClass
{
    /// <summary>Not a Library file: sync noise, media, logs, unknown folders (decision 2).</summary>
    Ignored,

    /// <summary>A <c>.json</c> file in an object folder named by a GUID. Its contents must carry the same Id.</summary>
    ObjectFile,

    /// <summary><c>categories.json</c> or <c>library.json</c>.</summary>
    Singleton,

    /// <summary>A <c>.json</c> file in an object folder that is not named <c>{guid}.json</c>. Never loaded.</summary>
    Misnamed,
}

public readonly record struct ClassifiedFile(
    LibraryFileClass Class,
    LibraryFileKind Kind = default,
    Guid Id = default,
    ProblemKind Problem = default);

/// <summary>Decides what a path inside the Library folder is (LIB-009, ARCH 9.1).</summary>
public static partial class LibraryFileNames
{
    [GeneratedRegex(@"\(\d+\)")]
    private static partial Regex NumberedCopy();

    public static ClassifiedFile Classify(LibraryLayout layout, string fullPath)
    {
        var relative = Path.GetRelativePath(layout.Root, fullPath);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return new(LibraryFileClass.Ignored);

        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = parts[^1];
        if (!string.Equals(Path.GetExtension(name), ".json", StringComparison.OrdinalIgnoreCase) || name.StartsWith("~$", StringComparison.Ordinal))
            return new(LibraryFileClass.Ignored);

        if (parts.Length == 1)
        {
            if (string.Equals(name, Path.GetFileName(layout.CategoriesFile), StringComparison.OrdinalIgnoreCase))
                return new(LibraryFileClass.Singleton, LibraryFileKind.Categories);
            if (string.Equals(name, Path.GetFileName(layout.SettingsFile), StringComparison.OrdinalIgnoreCase))
                return new(LibraryFileClass.Singleton, LibraryFileKind.Settings);
            return new(LibraryFileClass.Ignored);
        }

        if (parts.Length != 2 || KindOfFolder(parts[0]) is not { } kind)
            return new(LibraryFileClass.Ignored);

        var stem = Path.GetFileNameWithoutExtension(name);
        if (Guid.TryParseExact(stem, "D", out var id))
            return new(LibraryFileClass.ObjectFile, kind, id);

        var problem = NumberedCopy().IsMatch(stem) || stem.Contains("conflict", StringComparison.OrdinalIgnoreCase)
            ? ProblemKind.ConflictCopy
            : ProblemKind.UnexpectedFile;
        return new(LibraryFileClass.Misnamed, kind, Problem: problem);
    }

    private static LibraryFileKind? KindOfFolder(string folder) => folder.ToLowerInvariant() switch
    {
        "shows" => LibraryFileKind.Show,
        "services" => LibraryFileKind.Service,
        "templates" => LibraryFileKind.Template,
        "service-templates" => LibraryFileKind.ServiceTemplate,
        "service-state" => LibraryFileKind.ServiceState,
        _ => null,
    };
}
