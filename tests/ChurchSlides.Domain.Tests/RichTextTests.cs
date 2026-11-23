using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Text;

namespace ChurchSlides.Domain.Tests;

public class RichTextTests
{
    private static Paragraph P(params Inline[] inlines) => new() { Inlines = [.. inlines] };
    private static TextRun T(string s) => new() { Text = s };

    [Fact]
    public void Empty_rich_text_has_no_paragraphs_and_empty_plain_text()
    {
        Assert.Empty(RichText.Empty.Paragraphs);
        Assert.Equal("", RichText.Empty.ToPlainText());
    }

    [Fact]
    public void Plain_text_joins_runs_and_separates_paragraphs_with_newlines()
    {
        var text = new RichText
        {
            Paragraphs = [P(T("Amazing "), new TextRun { Text = "grace", Format = new RunFormat { Bold = true } }), P(T("how sweet"))]
        };
        Assert.Equal("Amazing grace\nhow sweet", text.ToPlainText());
    }

    [Fact]
    public void Tokens_render_as_markup_in_plain_text()
    {
        var text = new RichText
        {
            Paragraphs =
            [
                P(T("Preacher: "), new TokenRun { Token = new VariableToken { Name = "preacher" } }),
                P(new TokenRun { Token = new TimerToken { Name = "ServiceStart" } }),
                P(new TokenRun { Token = new ShowMetadataToken { Field = ShowMetadataField.Title } }),
                P(new TokenRun { Token = new ChurchLicenseToken() }),
            ]
        };
        Assert.Equal("Preacher: {{preacher}}\n{{timer:ServiceStart}}\n{{show.title}}\n{{church.ccliLicense}}", text.ToPlainText());
    }

    [Theory]
    [InlineData(ShowMetadataField.Title, "{{show.title}}")]
    [InlineData(ShowMetadataField.Authors, "{{show.authors}}")]
    [InlineData(ShowMetadataField.Copyright, "{{show.copyright}}")]
    [InlineData(ShowMetadataField.Ccli, "{{show.ccli}}")]
    public void Show_metadata_tokens_use_the_ccli_003_markup(ShowMetadataField field, string markup)
    {
        Assert.Equal(markup, new ShowMetadataToken { Field = field }.ToMarkup());
    }

    [Fact]
    public void Run_format_defaults_to_no_overrides()
    {
        var f = new RunFormat();
        Assert.Null(f.FontFamily);
        Assert.Null(f.FontSize);
        Assert.Null(f.ColorHex);
        Assert.Null(f.Bold);
        Assert.Null(f.Italic);
    }

    [Fact]
    public void Default_copyright_placement_follows_show_kind()
    {
        // SHOW-004 (assumption): Songs show it on the first slide, Presentations don't show it.
        Assert.Equal(CopyrightPlacement.FirstSlide, Show.DefaultCopyrightPlacement(ShowKind.Song));
        Assert.Equal(CopyrightPlacement.None, Show.DefaultCopyrightPlacement(ShowKind.Presentation));
    }

    [Fact]
    public void Typed_ids_are_distinct_values_that_print_as_guids()
    {
        var g = Guid.NewGuid();
        Id<Show> showId = new(g);
        Assert.Equal(g.ToString(), showId.ToString());
        Assert.NotEqual(Id<Show>.New(), Id<Show>.New());
        Assert.True(default(Id<Show>).IsEmpty);
    }
}
