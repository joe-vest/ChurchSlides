using ChurchSlides.Domain;
using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Text;

namespace ChurchSlides.Storage.Tests;

/// <summary>A fresh empty folder per test, deleted afterwards. The name has spaces and accents on purpose (LIB-001).</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder() =>
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ChurchSlidesTests", Guid.NewGuid().ToString("N"), "Église de Paix");

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    /// <summary>Every file under the folder, relative and with forward slashes, sorted.</summary>
    public string[] Files() =>
        !Directory.Exists(Path)
            ? []
            : [.. Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories)
                .Select(f => System.IO.Path.GetRelativePath(Path, f).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)];

    public void Dispose()
    {
        try { Directory.Delete(System.IO.Path.GetDirectoryName(Path)!, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

internal static class Samples
{
    public static Id<T> I<T>(int n) => new(Guid.Parse($"00000000-0000-0000-0000-{n:D12}"));

    public static Show Show(int n = 1, string title = "Amazing Grace") => new()
    {
        Id = I<Show>(n),
        Title = title,
        Kind = ShowKind.Song,
        CategoryId = I<Category>(2),
        CopyrightPlacement = CopyrightPlacement.FirstSlide,
        Slides =
        [
            new Slide
            {
                Id = I<Slide>(n * 100 + 1),
                Label = "Verse 1",
                TextBoxes =
                [
                    new SlideTextBox
                    {
                        Id = I<SlideTextBox>(n * 100 + 2),
                        TemplateBoxName = "body",
                        Content = new RichText { Paragraphs = [new Paragraph { Inlines = [new TextRun { Text = "Amazing grace, señor" }] }] },
                    },
                ],
            },
        ],
    };

    public static Template Template(int n = 3, string name = "Hymns") => new()
    {
        Id = I<Template>(n),
        Name = name,
        Background = new Background
        {
            Kind = MediaKind.Image,
            MediaPath = LibraryRelativePath.Create("media/images/hymn-bg.png"),
        },
        Boxes = [new TemplateBox { Name = "body", Geometry = new RectRelative(0.05, 0.1, 0.9, 0.7) }],
    };

    public static Service Service(int n = 7, string name = "Sunday") => new()
    {
        Id = I<Service>(n),
        Name = name,
        Date = new DateOnly(2026, 11, 29),
        Items =
        [
            new ShowReferenceItem { Id = I<ServiceItem>(n * 10 + 1), ShowId = I<Show>(1) },
            new ShowCopyItem { Id = I<ServiceItem>(n * 10 + 2), LocalShow = Show(9, "Local copy"), OriginShowId = I<Show>(1) },
        ],
        Variables = [new Variable { Name = "preacher", Value = "Pastor Ana" }],
    };

    public static ServiceTemplate ServiceTemplate(int n = 8, string name = "Weekly") => new()
    {
        Id = I<ServiceTemplate>(n),
        Name = name,
        Items = [new PlaceholderItem { Id = I<ServiceItem>(n * 10 + 1), Placeholder = new PlaceholderSpec { Label = "Opening song" } }],
    };

    public static CategoryList Categories() => new()
    {
        Categories = [new Category { Id = I<Category>(2), Name = "Hymns" }, new Category { Id = I<Category>(6), Name = "Welcome" }],
    };

}
