using System.Collections.Immutable;
using System.Text.RegularExpressions;
using ChurchSlides.Domain.Text;

namespace ChurchSlides.Domain.Resolution;

/// <summary>Variable and Timer names: letters, digits and underscores, case-insensitive (VAR-001).</summary>
public static class VariableNames
{
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    public static bool IsValid(string? name) =>
        !string.IsNullOrEmpty(name) && name.All(c => char.IsLetterOrDigit(c) || c == '_');
}

/// <summary>
/// Turns text containing <c>{{...}}</c> markup into text and token runs. Anything in double braces that isn't a
/// recognized token stays as literal text.
/// </summary>
public static partial class TokenMarkup
{
    [GeneratedRegex(@"\{\{([^{}]*)\}\}")]
    private static partial Regex Braces();

    public static ImmutableArray<Inline> Parse(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];

        var inlines = ImmutableArray.CreateBuilder<Inline>();
        var position = 0;

        foreach (Match match in Braces().Matches(text))
        {
            if (TryParseToken(match.Groups[1].Value.Trim()) is not { } token) continue;

            if (match.Index > position) inlines.Add(new TextRun { Text = text[position..match.Index] });
            inlines.Add(new TokenRun { Token = token });
            position = match.Index + match.Length;
        }

        if (position < text.Length) inlines.Add(new TextRun { Text = text[position..] });
        return inlines.ToImmutable();
    }

    private static Token? TryParseToken(string inner)
    {
        if (inner.Equals("church.ccliLicense", StringComparison.OrdinalIgnoreCase))
            return new ChurchLicenseToken();

        if (inner.StartsWith("show.", StringComparison.OrdinalIgnoreCase))
        {
            return inner[5..].ToLowerInvariant() switch
            {
                "title" => new ShowMetadataToken { Field = ShowMetadataField.Title },
                "authors" => new ShowMetadataToken { Field = ShowMetadataField.Authors },
                "copyright" => new ShowMetadataToken { Field = ShowMetadataField.Copyright },
                "ccli" => new ShowMetadataToken { Field = ShowMetadataField.Ccli },
                _ => null,
            };
        }

        if (inner.StartsWith("timer:", StringComparison.OrdinalIgnoreCase))
        {
            var name = inner[6..].Trim();
            return VariableNames.IsValid(name) ? new TimerToken { Name = name } : null;
        }

        return VariableNames.IsValid(inner) ? new VariableToken { Name = inner } : null;
    }
}
