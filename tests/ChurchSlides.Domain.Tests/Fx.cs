using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Resolution;
using ChurchSlides.Domain.Text;

namespace ChurchSlides.Domain.Tests;

/// <summary>Small builders for resolution tests.</summary>
internal static class Fx
{
    public static Id<T> I<T>(int n) => SampleData.I<T>(n);

    public static readonly RectRelative DefaultBodyGeometry = new(0.1, 0.1, 0.8, 0.7);
    public static readonly RectRelative CopyrightGeometry = new(0.05, 0.9, 0.9, 0.07);

    public static Template Tpl(
        int n,
        string background = "#112233",
        RectRelative? body = null,
        string bodyColor = "#EEEEEE",
        bool copyrightBox = true)
    {
        var boxes = new List<TemplateBox>
        {
            new()
            {
                Name = "body",
                Geometry = body ?? DefaultBodyGeometry,
                Defaults = new TextDefaults { FontFamily = "Georgia", FontSizeRelative = 0.08, ColorHex = bodyColor },
            },
        };
        if (copyrightBox)
        {
            boxes.Add(new TemplateBox
            {
                Name = "copyright",
                Geometry = CopyrightGeometry,
                Defaults = new TextDefaults { FontSizeRelative = 0.025, ColorHex = "#CCCCCC" },
                IsCopyrightBox = true,
            });
        }

        return new Template
        {
            Id = I<Template>(n),
            Name = $"T{n}",
            Background = new Background { Kind = MediaKind.SolidColor, ColorHex = background },
            Boxes = [.. boxes],
        };
    }

    public static TextRun Run(string text, RunFormat? format = null) => new() { Text = text, Format = format };

    public static TokenRun Tok(Token token, RunFormat? format = null) => new() { Token = token, Format = format };

    public static RichText Rich(params Inline[] inlines) =>
        new() { Paragraphs = [new Paragraph { Inlines = [.. inlines] }] };

    public static SlideTextBox Box(RichText content, string? name = "body", RectRelative? geometry = null, int id = 1) => new()
    {
        Id = I<SlideTextBox>(id),
        TemplateBoxName = name,
        Geometry = geometry,
        Content = content,
    };

    public static Slide SlideOf(int n, params SlideTextBox[] boxes) => new()
    {
        Id = I<Slide>(n),
        Label = $"S{n}",
        TextBoxes = [.. boxes],
    };

    public static Show ShowOf(
        IEnumerable<Slide> slides,
        ShowKind kind = ShowKind.Song,
        CopyrightPlacement placement = CopyrightPlacement.FirstSlide,
        SongMetadata? metadata = null,
        Id<Template>? defaultTemplate = null,
        Id<Category>? category = null) => new()
        {
            Id = I<Show>(1),
            Title = "Amazing Grace",
            Kind = kind,
            CategoryId = category ?? I<Category>(1),
            CopyrightPlacement = placement,
            Metadata = metadata,
            DefaultTemplateId = defaultTemplate,
            Slides = [.. slides],
        };

    /// <summary>A Song with slides 1..count (Slide IDs I(1)..I(count)), those listed in <paramref name="marked"/> Skipped by default.</summary>
    public static Show Verses(int count, params int[] marked) => ShowOf(
        Enumerable.Range(1, count).Select(n => SlideOf(n) with { SkippedByDefault = marked.Contains(n) }));

    public static Id<Slide> S(int n) => I<Slide>(n);

    public static TemplateSet Set(IEnumerable<Template> templates, Id<Template>? libraryDefault = null, Id<Template>? categoryDefault = null) =>
        new(templates,
            [new Category { Id = I<Category>(1), Name = "Hymns", DefaultTemplateId = categoryDefault }],
            libraryDefault);

    public static ResolutionContext Ctx(
        Show show,
        TemplateSet set,
        Service? service = null,
        LibrarySettings? library = null,
        SlidePosition position = default) => new()
        {
            Templates = set,
            Library = library ?? new LibrarySettings(),
            Show = show,
            Service = service,
            Position = position,
        };

    public static Service ServiceWith(IEnumerable<Variable>? variables = null, IEnumerable<CountdownTimer>? timers = null) => new()
    {
        Id = I<Service>(1),
        Name = "Sunday",
        Date = new DateOnly(2026, 12, 6),
        Variables = [.. variables ?? []],
        Timers = [.. timers ?? []],
    };

    /// <summary>Joined text of every run in a render text box.</summary>
    public static string TextOf(RenderTextBox box) =>
        string.Join("\n", box.Paragraphs.Select(p => string.Concat(p.Runs.OfType<RenderTextRun>().Select(r => r.Text))));
}
