namespace ChurchSlides.Storage.Watching;

public enum ChangeType { Added, Changed, Removed }

/// <summary>One Library file that changed. <see cref="Id"/> is null for <c>categories.json</c> and <c>library.json</c>. <see cref="Stamp"/> is null when removed.</summary>
public sealed record LibraryChange(LibraryFileKind Kind, Guid? Id, ChangeType Type, string Path, FileStamp? Stamp);

public enum ProblemKind
{
    /// <summary>Looks like a sync tool's copy: <c>(1)</c> or "conflict" in the name.</summary>
    ConflictCopy,

    /// <summary>Named like an object but holding another Id (copied or renamed by hand).</summary>
    CopiedFile,

    /// <summary>A <c>.json</c> file in an object folder that isn't named by an Id.</summary>
    UnexpectedFile,

    /// <summary>Damaged, or held open by another program.</summary>
    Unreadable,
}

/// <summary>A file in the Library folder that is not loaded (LIB-009).</summary>
public sealed record LibraryProblem(string Path, ProblemKind Kind, string Message);

/// <summary>What <see cref="LibraryMonitor.Apply"/> found. <see cref="Unavailable"/> means the Library folder itself is gone.</summary>
public sealed record LibraryUpdate(
    IReadOnlyList<LibraryChange> Changes,
    IReadOnlyList<LibraryProblem> Problems,
    bool ProblemsChanged,
    bool Unavailable = false)
{
    public bool IsEmpty => Changes.Count == 0 && !ProblemsChanged && !Unavailable;
}
