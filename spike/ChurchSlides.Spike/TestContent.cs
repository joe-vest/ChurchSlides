using System.Windows.Media.Imaging;

namespace ChurchSlides.Spike;

/// <summary>Backgrounds and the logo found in the media folder. The logo is any file named logo.*.</summary>
public sealed record Media(IReadOnlyList<string> Backgrounds, string? Logo)
{
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".bmp"];

    public static Media Scan(string? folder)
    {
        if (folder is null || !Directory.Exists(folder))
            return new Media([], null);
        var images = Directory.EnumerateFiles(folder)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        static bool IsLogo(string f) => Path.GetFileNameWithoutExtension(f).Equals("logo", StringComparison.OrdinalIgnoreCase);
        return new Media(images.Where(f => !IsLogo(f)).ToList(), images.FirstOrDefault(IsLogo));
    }

    /// <summary>The logo decoded at output size (Fit), or null: Logo then shows black (LIVE-012).</summary>
    public BitmapSource? LogoBitmap(int width, int height) =>
        Logo is { } path ? new SlideFactory(width, height).Background(path, FitMode.Fit) : null;
}

/// <summary>Public-domain texts (decision 7) and the slide sets built from them.</summary>
public static class TestContent
{
    public static readonly string[] AmazingGrace =
    [
        "Amazing grace! How sweet the sound\nThat saved a wretch like me!\nI once was lost, but now am found;\nWas blind, but now I see.",
        "'Twas grace that taught my heart to fear,\nAnd grace my fears relieved;\nHow precious did that grace appear\nThe hour I first believed.",
        "Through many dangers, toils and snares,\nI have already come;\n'Tis grace hath brought me safe thus far,\nAnd grace will lead me home.",
        "The Lord has promised good to me,\nHis Word my hope secures;\nHe will my Shield and Portion be,\nAs long as life endures.",
        "Yea, when this flesh and heart shall fail,\nAnd mortal life shall cease,\nI shall possess, within the veil,\nA life of joy and peace.",
        "When we've been there ten thousand years,\nBright shining as the sun,\nWe've no less days to sing God's praise\nThan when we'd first begun.",
    ];

    private const string ItIsWellChorus = "It is well with my soul,\nIt is well, it is well with my soul.";

    public static readonly string[] ItIsWell =
    [
        "When peace like a river attendeth my way,\nWhen sorrows like sea billows roll;\nWhatever my lot, Thou hast taught me to say,\nIt is well, it is well with my soul.",
        ItIsWellChorus,
        "Though Satan should buffet, though trials should come,\nLet this blest assurance control,\nThat Christ has regarded my helpless estate,\nAnd hath shed His own blood for my soul.",
        "My sin, oh, the bliss of this glorious thought!\nMy sin, not in part but the whole,\nIs nailed to the cross, and I bear it no more,\nPraise the Lord, praise the Lord, O my soul!",
        "And Lord, haste the day when my faith shall be sight,\nThe clouds be rolled back as a scroll;\nThe trump shall resound, and the Lord shall descend,\nEven so, it is well with my soul.",
        ItIsWellChorus,
    ];

    public static readonly string[] HolyHolyHoly =
    [
        "Holy, Holy, Holy",
        "Holy, holy, holy! Lord God Almighty!\nEarly in the morning our song shall rise to Thee;\nHoly, holy, holy, merciful and mighty!\nGod in three Persons, blessed Trinity!",
        "Holy, holy, holy! All the saints adore Thee,\nCasting down their golden crowns around the glassy sea;\nCherubim and seraphim falling down before Thee,\nWho wert, and art, and evermore shalt be.",
        "Holy, holy, holy! Though the darkness hide Thee,\nThough the eye of sinful man Thy glory may not see;\nOnly Thou art holy; there is none beside Thee,\nPerfect in power, in love, and purity.",
        "Holy, holy, holy! Lord God Almighty!\nAll Thy works shall praise Thy Name, in earth, and sky, and sea;\nHoly, holy, holy; merciful and mighty!\nGod in three Persons, blessed Trinity!",
        "Amen.",
    ];

    /// <summary>Psalm 23, KJV, as four Scripture slides.</summary>
    public static readonly string[] Psalm23 =
    [
        "The LORD is my shepherd; I shall not want. He maketh me to lie down in green pastures: he leadeth me beside the still waters.\nPsalm 23:1-2",
        "He restoreth my soul: he leadeth me in the paths of righteousness for his name's sake.\nPsalm 23:3",
        "Yea, though I walk through the valley of the shadow of death, I will fear no evil: for thou art with me; thy rod and thy staff they comfort me.\nPsalm 23:4",
        "Thou preparest a table before me in the presence of mine enemies: thou anointest my head with oil; my cup runneth over. Surely goodness and mercy shall follow me all the days of my life: and I will dwell in the house of the LORD for ever.\nPsalm 23:5-6",
    ];

    private static readonly string[] PresentationTitles = ["Welcome", "Notices", "Prayer", "Offering", "Communion"];

    /// <summary>The three hymns in a row, the background changing every three slides.</summary>
    public static List<SlideSpec> HymnSlides(IReadOnlyList<string> backgrounds, FitMode fit)
    {
        var texts = AmazingGrace.Concat(ItIsWell).Concat(HolyHolyHoly).ToList();
        return texts.Select((t, i) => new SlideSpec(t, backgrounds[i / 3 % backgrounds.Count], fit)).ToList();
    }

    /// <summary>
    /// The S6 typical Service: 3 Songs of 6 slides, 5 Presentations of 5 slides and a Scripture
    /// item of 4 slides, 47 slides. Each item has its own background (round robin), all Fill.
    /// </summary>
    public static List<SlideSpec> TypicalService(IReadOnlyList<string> backgrounds)
    {
        var items = new List<string[]> { AmazingGrace };
        foreach (var title in PresentationTitles.Take(2))
            items.Add(Presentation(title));
        items.Add(ItIsWell);
        items.Add(Psalm23);
        foreach (var title in PresentationTitles.Skip(2))
            items.Add(Presentation(title));
        items.Add(HolyHolyHoly);

        var slides = new List<SlideSpec>();
        for (var item = 0; item < items.Count; item++)
            slides.AddRange(items[item].Select(t => new SlideSpec(t, backgrounds[item % backgrounds.Count], FitMode.Fill)));
        return slides;
    }

    private static string[] Presentation(string title) =>
    [
        title,
        $"{title}\nA short line of text for the congregation to read.",
        $"{title}\nFirst point of three\nSecond point of three\nThird point of three",
        $"{title}\nSunday 10:30 in the main hall. Tea and coffee afterwards. Everyone is welcome.",
        $"{title}\nThank you.",
    ];
}
