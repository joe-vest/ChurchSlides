using ChurchSlides.Domain.Model;
using ChurchSlides.Storage.Migration;

namespace ChurchSlides.Storage;

/// <summary>Everything stored in the Library folder (LIB-001, LIB-002). Engine and the app code against this, so tests can fake it.</summary>
public interface ILibrary
{
    LibraryLayout Layout { get; }
    IObjectRepository<Show> Shows { get; }
    IObjectRepository<Service> Services { get; }
    IObjectRepository<Template> Templates { get; }
    IObjectRepository<ServiceTemplate> ServiceTemplates { get; }
    IServiceStateRepository ServiceStates { get; }
    ISingletonRepository<CategoryList> Categories { get; }
    ISingletonRepository<LibrarySettings> Settings { get; }
}

/// <summary>The Library as plain JSON files in a folder. Creates folders on the first save, not on open.</summary>
public sealed class FolderLibrary : ILibrary
{
    public FolderLibrary(string root, AtomicWriteOptions? writeOptions = null, MigrationChain? migrations = null)
    {
        Layout = new LibraryLayout(root);
        var files = new LibraryFileStore(new AtomicFileWriter(writeOptions), migrations ?? new MigrationChain());

        Shows = new ObjectRepository<Show>(Layout.ShowsFolder, s => s.Id, SaveValidation.Validate, files);
        Services = new ObjectRepository<Service>(Layout.ServicesFolder, s => s.Id, SaveValidation.Validate, files);
        Templates = new ObjectRepository<Template>(Layout.TemplatesFolder, t => t.Id, SaveValidation.Validate, files);
        ServiceTemplates = new ObjectRepository<ServiceTemplate>(Layout.ServiceTemplatesFolder, t => t.Id, SaveValidation.Validate, files);
        ServiceStates = new ServiceStateRepository(Layout.ServiceStateFolder, files);
        // CAT-010: the Scripture Category is added in memory. Loading never writes a file.
        Categories = new SingletonRepository<CategoryList>(Layout.CategoriesFile, SaveValidation.Validate, files, BuiltInCategories.EnsureIn);
        Settings = new SingletonRepository<LibrarySettings>(Layout.SettingsFile, SaveValidation.Validate, files);
    }

    public LibraryLayout Layout { get; }
    public IObjectRepository<Show> Shows { get; }
    public IObjectRepository<Service> Services { get; }
    public IObjectRepository<Template> Templates { get; }
    public IObjectRepository<ServiceTemplate> ServiceTemplates { get; }
    public IServiceStateRepository ServiceStates { get; }
    public ISingletonRepository<CategoryList> Categories { get; }
    public ISingletonRepository<LibrarySettings> Settings { get; }
}
