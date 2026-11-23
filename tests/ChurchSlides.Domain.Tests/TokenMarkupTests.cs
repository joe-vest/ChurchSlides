using ChurchSlides.Domain.Resolution;
using ChurchSlides.Domain.Text;

namespace ChurchSlides.Domain.Tests;

public class TokenMarkupTests
{
    private static string RoundTrip(string markup) =>
        new RichText { Paragraphs = [new Paragraph { Inlines = TokenMarkup.Parse(markup) }] }.ToPlainText();

    [Theory]
    [InlineData("{{preacher}}", typeof(VariableToken))]
    [InlineData("{{sermon_title}}", typeof(VariableToken))]
    [InlineData("{{timer:ServiceStart}}", typeof(TimerToken))]
    [InlineData("{{show.title}}", typeof(ShowMetadataToken))]
    [InlineData("{{show.authors}}", typeof(ShowMetadataToken))]
    [InlineData("{{show.copyright}}", typeof(ShowMetadataToken))]
    [InlineData("{{show.ccli}}", typeof(ShowMetadataToken))]
    [InlineData("{{church.ccliLicense}}", typeof(ChurchLicenseToken))]
    public void Each_token_form_parses_to_its_token_type(string markup, Type tokenType)
    {
        var inline = Assert.Single(TokenMarkup.Parse(markup));
        Assert.IsType(tokenType, Assert.IsType<TokenRun>(inline).Token);
    }

    [Theory]
    [InlineData("{{preacher}}")]
    [InlineData("{{timer:ServiceStart}}")]
    [InlineData("{{show.title}}")]
    [InlineData("{{show.authors}}")]
    [InlineData("{{show.copyright}}")]
    [InlineData("{{show.ccli}}")]
    [InlineData("{{church.ccliLicense}}")]
    [InlineData("Speaker: {{preacher}} at {{timer:ServiceStart}}!")]
    [InlineData("{{a}}{{b}}")]
    [InlineData("no tokens at all")]
    public void Markup_round_trips_through_the_token_model(string markup)
    {
        Assert.Equal(markup, RoundTrip(markup));
    }

    [Theory]
    [InlineData("{{SHOW.Title}}", "{{show.title}}")]
    [InlineData("{{ Church.CCLILicense }}", "{{church.ccliLicense}}")]
    [InlineData("{{ preacher }}", "{{preacher}}")]
    [InlineData("{{TIMER:ServiceStart}}", "{{timer:ServiceStart}}")]
    public void Keywords_match_ignoring_case_and_surrounding_spaces(string typed, string canonical)
    {
        Assert.Equal(canonical, RoundTrip(typed));
    }

    [Theory]
    [InlineData("{{unknown.thing}}")]
    [InlineData("{{show.nope}}")]
    [InlineData("{{a b}}")]
    [InlineData("{{timer:bad name}}")]
    [InlineData("{{timer:}}")]
    [InlineData("{{ }}")]
    [InlineData("{{}}")]
    [InlineData("{{abc")]
    [InlineData("abc}}")]
    [InlineData("{single}")]
    public void Unrecognized_braces_stay_as_literal_text(string text)
    {
        var inline = Assert.Single(TokenMarkup.Parse(text));
        Assert.Equal(text, Assert.IsType<TextRun>(inline).Text);
    }

    [Fact]
    public void Adjacent_tokens_produce_no_empty_text_runs()
    {
        var inlines = TokenMarkup.Parse("{{a}}{{b}}");
        Assert.Equal(2, inlines.Length);
        Assert.All(inlines, i => Assert.IsType<TokenRun>(i));
    }

    [Fact]
    public void Text_around_tokens_is_kept_in_order()
    {
        var inlines = TokenMarkup.Parse("before {{x}} after");

        Assert.Equal("before ", Assert.IsType<TextRun>(inlines[0]).Text);
        Assert.IsType<TokenRun>(inlines[1]);
        Assert.Equal(" after", Assert.IsType<TextRun>(inlines[2]).Text);
    }

    [Fact]
    public void Extra_braces_around_a_token_stay_as_text()
    {
        Assert.Equal("{{{preacher}}}", RoundTrip("{{{preacher}}}"));
        Assert.Equal(3, TokenMarkup.Parse("{{{preacher}}}").Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_input_gives_no_inlines(string? text)
    {
        Assert.Empty(TokenMarkup.Parse(text));
    }

    [Fact]
    public void Parsed_runs_carry_no_formatting()
    {
        Assert.All(TokenMarkup.Parse("a {{b}} c"), i => Assert.Null(i.Format));
    }
}
