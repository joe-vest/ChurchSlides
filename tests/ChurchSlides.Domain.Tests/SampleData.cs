using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Text;

namespace ChurchSlides.Domain.Tests;

/// <summary>Fully populated, deterministic objects used for round-trip and golden-file tests.</summary>
internal static class SampleData
{
    public static Guid G(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");
    public static Id<T> I<T>(int n) => new(G(n));

    public static readonly Id<Slide> Verse1 = I<Slide>(10);
    public static readonly Id<Slide> Chorus = I<Slide>(11);
    public static readonly Id<Slide> Verse2 = I<Slide>(12);

    private static Paragraph P(params Inline[] inlines) => new() { Inlines = [.. inlines] };
    private static TextRun T(string text, RunFormat? format = null) => new() { Text = text, Format = format };
    private static TokenRun Tok(Token token) => new() { Token = token };

    public static Show Song() => new()
    {
        Id = I<Show>(1),
        Title = "Amazing Grace — Señor",
        Kind = ShowKind.Song,
        CategoryId = I<Category>(2),
        CopyrightPlacement = CopyrightPlacement.FirstSlide,
        Tags = ["hymn", "communion"],
        Metadata = new SongMetadata
        {
            Authors = "John Newton",
            Copyright = "Public domain",
            CcliSongNumber = "22025",
            IsPublicDomain = true,
        },
        DefaultTemplateId = I<Template>(3),
        Slides =
        [
            new Slide
            {
                Id = Verse1,
                Label = "Verse 1",
                TextBoxes =
                [
                    new SlideTextBox
                    {
                        Id = I<SlideTextBox>(20),
                        TemplateBoxName = "body",
                        Content = new RichText
                        {
                            Paragraphs =
                            [
                                P(T("Amazing grace, how "), T("sweet", new RunFormat { Bold = true }), T(" the sound")),
                                P(T("That saved a wretch like me", new RunFormat { Italic = true, ColorHex = "#FF0000", FontFamily = "Georgia", FontSize = 0.06 })),
                            ],
                        },
                    },
                ],
            },
            new Slide
            {
                Id = Chorus,
                Label = "Chorus",
                TemplateOverrideId = I<Template>(4),
                BackgroundOverride = new Background
                {
                    Kind = MediaKind.Image,
                    MediaPath = LibraryRelativePath.Create("media/images/sunrise.jpg"),
                    Fit = BackgroundFit.Fit,
                },
                TextBoxes =
                [
                    new SlideTextBox
                    {
                        Id = I<SlideTextBox>(21),
                        TemplateBoxName = "body",
                        Geometry = new RectRelative(0.1, 0.2, 0.8, 0.5), // detached: own position, still remembers "body"
                        HAlign = HorizontalAlign.Left,
                        VAlign = VerticalAlign.Top,
                        LineSpacing = 1.2,
                        Content = new RichText { Paragraphs = [P(T("I once was lost, but now am found"))] },
                    },
                ],
                Actions =
                [
                    new TriggeredAction
                    {
                        Trigger = new SlideDisplayed(),
                        Delay = TimeSpan.FromSeconds(8),
                        Commands = [new NextSlide()],
                    },
                ],
            },
            new Slide
            {
                Id = Verse2,
                Label = "Verse 2",
                SkippedByDefault = true,
                BackgroundOverride = new Background { Kind = MediaKind.SolidColor, ColorHex = "#102030" },
                TextBoxes =
                [
                    new SlideTextBox
                    {
                        Id = I<SlideTextBox>(22),
                        TemplateBoxName = "body",
                        Content = new RichText
                        {
                            Paragraphs =
                            [
                                P(T("Preacher: "), Tok(new VariableToken { Name = "preacher" })),
                                P(Tok(new TimerToken { Name = "ServiceStart" })),
                                P(Tok(new ShowMetadataToken { Field = ShowMetadataField.Title }), T(" / "), Tok(new ChurchLicenseToken())),
                            ],
                        },
                    },
                ],
                Actions = [new TriggeredAction { Trigger = new SlideEnded(), Commands = [new Clear()] }],
            },
        ],
        Audio = new AudioTrack
        {
            MediaPath = LibraryRelativePath.Create("media/audio/amazing-grace.mp3"),
            TrimStart = TimeSpan.FromSeconds(2.4),
            TrimEnd = TimeSpan.FromSeconds(200),
            Cues = [new Cue(Chorus, TimeSpan.FromSeconds(31.5)), new Cue(Verse2, TimeSpan.FromSeconds(60))],
        },
        Actions =
        [
            new TriggeredAction { Trigger = new ShowStarted(), Commands = [new StartAudio()] },
            new TriggeredAction { Trigger = new AudioEnded(), Commands = [new NextItem()] },
        ],
    };

    public static Show SmallPresentation() => new()
    {
        Id = I<Show>(5),
        Title = "Welcome",
        Kind = ShowKind.Presentation,
        CategoryId = I<Category>(6),
        CopyrightPlacement = CopyrightPlacement.None,
        Slides =
        [
            new Slide
            {
                Id = I<Slide>(50),
                Label = "Welcome",
                TextBoxes =
                [
                    new SlideTextBox
                    {
                        Id = I<SlideTextBox>(51),
                        TemplateBoxName = "body",
                        Content = new RichText { Paragraphs = [P(T("Welcome to church"))] },
                    },
                ],
            },
        ],
    };

    public static Template Template() => new()
    {
        Id = I<Template>(3),
        Name = "Hymns",
        Background = new Background
        {
            Kind = MediaKind.Image,
            MediaPath = LibraryRelativePath.Create("media/images/hymn-bg.png"),
            Fit = BackgroundFit.Fill,
        },
        Boxes =
        [
            new TemplateBox
            {
                Name = "body",
                Geometry = new RectRelative(0.05, 0.1, 0.9, 0.7),
                Defaults = new TextDefaults
                {
                    FontFamily = "Georgia",
                    FontSizeRelative = 0.08,
                    ColorHex = "#FFFFFF",
                    Bold = true,
                    HAlign = HorizontalAlign.Center,
                    VAlign = VerticalAlign.Middle,
                    LineSpacing = 1.1,
                    Outline = new Outline("#000000", 0.002),
                    Shadow = new Shadow("#000000", 0.003, 0.003, 0.004),
                },
            },
            new TemplateBox
            {
                Name = "copyright",
                Geometry = new RectRelative(0.05, 0.9, 0.9, 0.08),
                Defaults = new TextDefaults { FontSizeRelative = 0.025, HAlign = HorizontalAlign.Left },
                IsCopyrightBox = true,
            },
        ],
    };

    /// <summary>One action holding every Command type.</summary>
    private static TriggeredAction EveryCommand() => new()
    {
        Trigger = new ServiceStarted(),
        Commands =
        [
            new NextSlide(),
            new PreviousSlide(),
            new NextItem(),
            new PreviousItem(),
            new GoToSlide { Target = new SlideTarget(Verse1, I<ServiceItem>(100)) },
            new GoToSlide { Target = new SlideTarget(Chorus) },
            new GoToItem { ItemId = I<ServiceItem>(102) },
            new StartAudio(),
            new StopAudio(),
            new Clear(),
            new Black(),
            new Logo(),
            new StartTimer { TimerName = "ServiceStart" },
            new StopTimer { TimerName = "Offering" },
        ],
    };

    public static Service Service() => new()
    {
        Id = I<Service>(7),
        Name = "Sunday Morning",
        Date = new DateOnly(2026, 12, 6),
        Mode = ServiceMode.Live,
        CreatedFromServiceTemplateId = I<ServiceTemplate>(8),
        Items =
        [
            new ShowReferenceItem
            {
                Id = I<ServiceItem>(100),
                ShowId = I<Show>(1),
                SkippedSlideIds = [Chorus],
                TemplateOverrideId = I<Template>(4),
                FilledFrom = new PlaceholderSpec { Label = "Opening song", CategoryFilter = I<Category>(2), KindFilter = ShowKind.Song },
                Actions = [new TriggeredAction { Trigger = new ShowEnded(), Commands = [new StopAudio()] }],
            },
            new ShowCopyItem
            {
                Id = I<ServiceItem>(101),
                LocalShow = SmallPresentation(),
                OriginShowId = I<Show>(5),
            },
            new PlaceholderItem
            {
                Id = I<ServiceItem>(102),
                Placeholder = new PlaceholderSpec { Label = "Communion liturgy", CategoryFilter = I<Category>(9) },
            },
            new ScriptureItem
            {
                Id = I<ServiceItem>(103),
                Scripture = new ScriptureSpec
                {
                    BibleId = "esv",
                    Reference = "Rom 8:28-39",
                    VersesPerSlide = 2,
                    ShowVerseNumbers = false,
                    ShowReferenceLine = true,
                    TemplateId = I<Template>(3),
                },
                Generated = SmallPresentation() with { Id = I<Show>(55), Title = "Romans 8:28-39" },
            },
        ],
        Variables = [new Variable { Name = "preacher", Value = "Pastor Sam" }, new Variable { Name = "sermon_title" }],
        Timers =
        [
            new CountdownTimer
            {
                Name = "ServiceStart",
                Type = TimerType.CountdownToClockTime,
                TargetClockTime = new TimeOnly(10, 0),
                EndBehavior = TimerEndBehavior.ContinueNegative,
                WarningThresholdSeconds = 60,
                WarningColorHex = "#FF0000",
            },
            new CountdownTimer
            {
                Name = "Offering",
                Type = TimerType.CountdownDuration,
                Duration = TimeSpan.FromMinutes(5),
                AutoStartOnDisplay = false,
            },
        ],
        Actions =
        [
            EveryCommand(),
            new TriggeredAction
            {
                Trigger = new TimerExpired { TimerName = "ServiceStart" },
                Delay = TimeSpan.FromMilliseconds(500),
                Commands = [new GoToItem { ItemId = I<ServiceItem>(101) }],
            },
        ],
    };

    public static ServiceTemplate ServiceTemplate() => new()
    {
        Id = I<ServiceTemplate>(8),
        Name = "Sunday Morning",
        Items =
        [
            new PlaceholderItem
            {
                Id = I<ServiceItem>(200),
                Placeholder = new PlaceholderSpec { Label = "Opening song", KindFilter = ShowKind.Song },
            },
            new ShowReferenceItem { Id = I<ServiceItem>(201), ShowId = I<Show>(5) },
            new ScriptureItem { Id = I<ServiceItem>(202), Scripture = new ScriptureSpec { BibleId = "esv", Reference = "Ps 23" } },
        ],
        Variables = [new Variable { Name = "preacher", Value = "TBD" }],
        Timers = [new CountdownTimer { Name = "ServiceStart", Type = TimerType.CountdownDuration, Duration = TimeSpan.FromMinutes(10) }],
        Actions = [new TriggeredAction { Trigger = new TimerExpired { TimerName = "ServiceStart" }, Commands = [new GoToItem { ItemId = I<ServiceItem>(201) }] }],
    };

    public static CategoryList Categories() => new()
    {
        Categories =
        [
            new Category { Id = I<Category>(2), Name = "Hymns", DefaultTemplateId = I<Template>(3) },
            new Category { Id = I<Category>(6), Name = "Announcements" },
        ],
    };

    public static LibrarySettings Settings() => new()
    {
        Canvas = CanvasAspect.Ratio16x9,
        CcliLicenseNumber = "1234567",
        LogoPath = LibraryRelativePath.Create("media/images/logo.png"),
        Transition = TransitionKind.Crossfade,
        CrossfadeMilliseconds = 400,
        DefaultTemplateId = I<Template>(3),
        DefaultScriptureTemplateId = I<Template>(4),
        CcliReportingPeriodStart = new DateOnly(2026, 1, 1),
        CcliReportingPeriodEnd = new DateOnly(2026, 12, 31),
    };
}
