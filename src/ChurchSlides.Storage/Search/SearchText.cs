using System.Globalization;
using System.Text;

namespace ChurchSlides.Storage.Search;

/// <summary>Makes text comparable for searching: lowercase, no accents, punctuation as spaces (CAT-003).</summary>
public static class SearchText
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var sb = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(char.ToLowerInvariant(c));
            }
            else pendingSpace = true;
        }
        return sb.ToString();
    }
}
