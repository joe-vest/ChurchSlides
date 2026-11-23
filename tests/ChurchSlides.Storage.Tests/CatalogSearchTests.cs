using System.Diagnostics;
using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Text;
using ChurchSlides.Storage.Search;

namespace ChurchSlides.Storage.Tests;

public class CatalogSearchTests
{
    private static Show MakeShow(int n, string title, string lyrics = "", string? ccli = null, ShowKind kind = ShowKind.Song, int category = 2, string label = "Verse 1")
    {
        var s = Samples.Show(n, title);
        var box = s.Slides[0].TextBoxes[0] with
        {
            Content = new RichText
            {
                Paragraphs = [new Paragraph { Inlines = [new TextRun { Text = lyrics }, new TokenRun { Token = new VariableToken { Name = "preacher" } }] }],
            },
        };
        return s with
        {
            Kind = kind,
            CategoryId = Samples.I<Category>(category),
            Metadata = ccli is null ? null : new SongMetadata { CcliSongNumber = ccli },
            Slides = [s.Slides[0] with { Label = label, TextBoxes = [box] }],
        };
    }

    private static CatalogSearch Index(params Show[] shows)
    {
        var search = new CatalogSearch();
        search.Load(shows);
        return search;
    }

    private static string[] Titles(CatalogSearch s, string text, ShowKind? kind = null, int? category = null) =>
        [.. s.Search(new SearchQuery(text, kind, category is null ? null : Samples.I<Category>(category.Value))).Select(h => h.Title)];

    private static string ShowFile(TempFolder temp, int n, string suffix = "") =>
        temp.Combine("shows", $"{Samples.I<Show>(n)}{suffix}.json");

    [Theory]
    [InlineData("Él Shaddai", "el shaddai")]
    [InlineData("SEÑOR", "senor")]
    [InlineData("Grâce", "grace")]
    [InlineData("  Amazing,  grace!! ", "amazing grace")]
    [InlineData("How Great Thou Art (3rd verse)", "how great thou art 3rd verse")]
    [InlineData("", "")]
    public void CAT_003_normalize_lowercases_strips_accents_and_turns_punctuation_into_spaces(string input, string expected) =>
        Assert.Equal(expected, SearchText.Normalize(input));

    [Fact]
    public void CAT_003_typing_narrows_the_list_each_letter()
    {
        var s = Index(MakeShow(1, "Amazing Grace"), MakeShow(2, "Amazed"), MakeShow(3, "Be Thou My Vision"));

        Assert.Equal(["Amazed", "Amazing Grace", "Be Thou My Vision"], Titles(s, "")); // empty lists all, sorted
        Assert.Equal(["Amazed", "Amazing Grace"], Titles(s, "am"));
        Assert.Equal(["Amazed", "Amazing Grace"], Titles(s, "ama"));
        Assert.Equal(["Amazing Grace"], Titles(s, "amazi"));
        Assert.Empty(Titles(s, "amazingx"));
    }

    [Fact]
    public void CAT_003_case_and_accents_are_ignored_both_ways()
    {
        var s = Index(MakeShow(1, "Señor, ten piedad"), MakeShow(2, "Senor Jesus"));

        Assert.Equal(["Senor Jesus", "Señor, ten piedad"], Titles(s, "senor"));
        Assert.Equal(["Senor Jesus", "Señor, ten piedad"], Titles(s, "SEÑOR"));
    }

    [Fact]
    public void CAT_004_title_start_beats_title_beats_ccli_beats_lyrics()
    {
        var s = Index(
            MakeShow(1, "Zeta", lyrics: "Grace is all"),
            MakeShow(2, "Zed", ccli: "grace"),
            MakeShow(3, "Amazing Grace"),
            MakeShow(4, "Grace Alone"));

        var hits = s.Search(new SearchQuery("grace"));

        Assert.Equal(["Grace Alone", "Amazing Grace", "Zed", "Zeta"], hits.Select(h => h.Title));
        Assert.Equal([SearchMatch.TitleStart, SearchMatch.Title, SearchMatch.CcliNumber, SearchMatch.SlideText], hits.Select(h => h.Match));
    }

    [Fact]
    public void CAT_004_words_match_in_any_order_and_the_ccli_number_matches_exactly()
    {
        var s = Index(MakeShow(1, "Amazing Grace", ccli: "22025"), MakeShow(2, "Other"));

        Assert.Equal(["Amazing Grace"], Titles(s, "grace amazing"));
        Assert.Equal(["Amazing Grace"], Titles(s, "22025"));
        Assert.Empty(Titles(s, "2202"));
        Assert.Empty(Titles(s, "grace other"));
    }

    [Fact]
    public void CAT_004_tokens_and_slide_labels_do_not_match()
    {
        var s = Index(MakeShow(1, "Song", lyrics: "la la", label: "Chorus"));

        Assert.Empty(Titles(s, "preacher"));
        Assert.Empty(Titles(s, "chorus"));
        Assert.Equal(["Song"], Titles(s, "la"));
    }

    [Fact]
    public void CAT_004_text_split_across_runs_in_one_paragraph_is_one_word()
    {
        var show = MakeShow(1, "Song");
        var box = show.Slides[0].TextBoxes[0] with
        {
            Content = new RichText { Paragraphs = [new Paragraph { Inlines = [new TextRun { Text = "Ama" }, new TextRun { Text = "zing" }] }] },
        };
        var s = Index(show with { Slides = [show.Slides[0] with { TextBoxes = [box] }] });

        Assert.Equal(["Song"], Titles(s, "amazing"));
    }

    [Fact]
    public void CAT_003_kind_and_category_filters_combine_with_the_query()
    {
        var s = Index(
            MakeShow(1, "Amazing Grace", category: 2),
            MakeShow(2, "Amazing Slides", kind: ShowKind.Presentation, category: 6),
            MakeShow(3, "Welcome", kind: ShowKind.Presentation, category: 6));

        Assert.Equal(["Amazing Slides", "Welcome"], Titles(s, "", ShowKind.Presentation));
        Assert.Equal(["Amazing Slides", "Welcome"], Titles(s, "", category: 6));
        Assert.Equal(["Amazing Slides"], Titles(s, "amazing", ShowKind.Presentation, 6));
        Assert.Empty(Titles(s, "amazing", ShowKind.Song, 6));
    }

    [Fact]
    public void CAT_005_upsert_moves_the_hit_and_remove_drops_it()
    {
        var s = Index(MakeShow(1, "Amazing Grace"), MakeShow(2, "Beloved"));

        s.Upsert(MakeShow(1, "Zion"));
        Assert.Equal(["Beloved", "Zion"], Titles(s, ""));
        Assert.Empty(Titles(s, "amazing"));

        s.Remove(Samples.I<Show>(2));
        Assert.Equal(["Zion"], Titles(s, ""));
    }

    [Fact]
    public void CAT_005_a_monitor_update_changes_the_index_and_problem_files_are_not_indexed()
    {
        using var temp = new TempFolder();
        var mine = new FolderLibrary(temp.Path);
        var other = new FolderLibrary(temp.Path);
        Assert.True(mine.Shows.Save(MakeShow(1, "Amazing Grace"), null).Succeeded);
        var second = mine.Shows.Save(MakeShow(2, "Beloved"), null);
        File.Copy(ShowFile(temp, 1), ShowFile(temp, 1, " (1)")); // conflict copy
        File.Copy(ShowFile(temp, 1), ShowFile(temp, 5)); // copied file: holds Id 1
        File.WriteAllText(ShowFile(temp, 6), "{ not json");

        var s = new CatalogSearch();
        s.Load(mine);
        Assert.Equal(["Amazing Grace", "Beloved"], Titles(s, ""));
        mine.Monitor.Scan();

        Assert.True(other.Shows.Save(MakeShow(1, "Renamed"), other.Shows.Load(Samples.I<Show>(1))!.Stamp).Succeeded);
        Assert.True(other.Shows.Delete(Samples.I<Show>(2), second.Stamp).Succeeded);
        Assert.True(other.Shows.Save(MakeShow(3, "Third"), null).Succeeded);
        s.Apply(mine.Monitor.Rescan(), mine);

        Assert.Equal(["Renamed", "Third"], Titles(s, ""));
    }

    [Fact]
    public void CAT_005_a_show_that_stops_loading_is_dropped()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        Assert.True(lib.Shows.Save(MakeShow(1, "Amazing Grace"), null).Succeeded);
        var s = new CatalogSearch();
        s.Load(lib);
        lib.Monitor.Scan();

        File.WriteAllText(ShowFile(temp, 1), "{ not json");
        s.Apply(lib.Monitor.Apply([ShowFile(temp, 1)]), lib);

        Assert.Empty(Titles(s, ""));
    }

    [Fact]
    public void CAT_003_two_thousand_shows_search_quickly()
    {
        var shows = Enumerable.Range(1, 2000).Select(i =>
            MakeShow(i, $"Song number {i}", lyrics: string.Join(" ", Enumerable.Range(0, 8).Select(k => $"line {k} of song {i} with some words"))));
        var s = Index([.. shows]);
        s.Search(new SearchQuery("a")); // warm up

        var watch = Stopwatch.StartNew();
        var hits = s.Search(new SearchQuery("e"));
        watch.Stop();

        Assert.Equal(2000, hits.Count);
        Assert.True(watch.ElapsedMilliseconds < 50, $"took {watch.ElapsedMilliseconds} ms");
    }
}
