namespace ChurchSlides.Storage;

public enum LibraryFolderStatus
{
    /// <summary>The folder exists. An empty one is a new Library.</summary>
    Ok,
    NotSet,
    Missing,
    NotAFolder,
}

/// <summary>Startup check of the chosen Library folder (LIB-010). The prompt to choose another one is UI.</summary>
public static class LibraryFolder
{
    public static LibraryFolderStatus Check(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return LibraryFolderStatus.NotSet;
        if (Directory.Exists(path)) return LibraryFolderStatus.Ok;
        return File.Exists(path) ? LibraryFolderStatus.NotAFolder : LibraryFolderStatus.Missing;
    }
}
