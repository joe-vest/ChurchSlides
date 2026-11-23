namespace ChurchSlides.Storage;

/// <summary>Where each kind of file lives inside a Library folder (LIB-002, FRD section 5).</summary>
public sealed class LibraryLayout
{
    public LibraryLayout(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    public string SettingsFile => Path.Combine(Root, "library.json");
    public string CategoriesFile => Path.Combine(Root, "categories.json");
    public string ShowsFolder => Path.Combine(Root, "shows");
    public string ServicesFolder => Path.Combine(Root, "services");
    public string TemplatesFolder => Path.Combine(Root, "templates");
    public string ServiceTemplatesFolder => Path.Combine(Root, "service-templates");
    public string MediaFolder => Path.Combine(Root, "media");
}
