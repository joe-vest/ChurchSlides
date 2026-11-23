using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Serialization;

namespace ChurchSlides.Domain.Text;

/// <summary>
/// Our own rich-text model (D-3): paragraphs of runs, with formatting on character ranges.
/// The WPF FlowDocument is only an editing surface; this is what gets saved.
/// </summary>
public sealed record RichText
{
    public static RichText Empty { get; } = new();

    [JsonRequired] public ImmutableArray<Paragraph> Paragraphs { get; init; } = [];

    /// <summary>Text without formatting. Tokens appear as their <c>{{...}}</c> markup.</summary>
    public string ToPlainText()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < Paragraphs.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            foreach (var inline in Paragraphs[i].Inlines)
            {
                sb.Append(inline switch
                {
                    TextRun run => run.Text,
                    TokenRun token => token.Token.ToMarkup(),
                    _ => "",
                });
            }
        }
        return sb.ToString();
    }
}

public sealed record Paragraph
{
    [JsonRequired] public ImmutableArray<Inline> Inlines { get; init; } = [];
}

/// <summary>
/// Formatting set explicitly on a run. Null means "not set": Template defaults apply (TPL-024).
/// Font size is a fraction of canvas height (SLD-005).
/// </summary>
public sealed record RunFormat
{
    public static RunFormat None { get; } = new();

    public string? FontFamily { get; init; }
    public double? FontSize { get; init; }
    public string? ColorHex { get; init; }
    public bool? Bold { get; init; }
    public bool? Italic { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TextRun), "text")]
[JsonDerivedType(typeof(TokenRun), "token")]
public abstract record Inline
{
    /// <summary>Null means no explicit formatting: Template defaults apply.</summary>
    public RunFormat? Format { get; init; }
}

public sealed record TextRun : Inline
{
    [JsonRequired] public string Text { get; init; } = "";
}

/// <summary>A variable, timer or metadata placeholder inside text (SLD-027). Formatting applies to its resolved value (VAR-006).</summary>
public sealed record TokenRun : Inline
{
    [JsonRequired] public Token Token { get; init; } = null!;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(VariableToken), "variable")]
[JsonDerivedType(typeof(TimerToken), "timer")]
[JsonDerivedType(typeof(ShowMetadataToken), "showMetadata")]
[JsonDerivedType(typeof(ChurchLicenseToken), "churchLicense")]
public abstract record Token
{
    /// <summary>The canonical text form, for example <c>{{preacher}}</c>.</summary>
    public abstract string ToMarkup();
}

public sealed record VariableToken : Token
{
    [JsonRequired] public string Name { get; init; } = "";
    public override string ToMarkup() => "{{" + Name + "}}";
}

public sealed record TimerToken : Token
{
    [JsonRequired] public string Name { get; init; } = "";
    public override string ToMarkup() => "{{timer:" + Name + "}}";
}

public enum ShowMetadataField { Title, Authors, Copyright, Ccli }

public sealed record ShowMetadataToken : Token
{
    [JsonRequired] public ShowMetadataField Field { get; init; }

    public override string ToMarkup() => Field switch
    {
        ShowMetadataField.Title => "{{show.title}}",
        ShowMetadataField.Authors => "{{show.authors}}",
        ShowMetadataField.Copyright => "{{show.copyright}}",
        ShowMetadataField.Ccli => "{{show.ccli}}",
        _ => throw new ArgumentOutOfRangeException(nameof(Field)),
    };
}

public sealed record ChurchLicenseToken : Token
{
    public override string ToMarkup() => "{{church.ccliLicense}}";
}
