using System.Net;
using System.Text;
using System.Text.Json.Serialization;

namespace WindowsQuotes.Quotes;

/// <summary>Where a quote came from. Online sources use their <see cref="Sources.IQuoteSource.Id"/>.</summary>
public static class QuoteOrigin
{
    /// <summary>Typed in by the user.</summary>
    public const string Mine = "mine";

    /// <summary>Saved from the widget with the star button.</summary>
    public const string Starred = "starred";

    public const string Starter = "starter";
}

public sealed record Quote(string Text, string Author, string Source = QuoteOrigin.Mine)
{
    /// <summary>Identity for de-duplication: the text, ignoring case, spacing and punctuation.</summary>
    [JsonIgnore]
    public string Key
    {
        get
        {
            var key = new StringBuilder(Text.Length);
            foreach (var c in Text)
            {
                if (char.IsLetterOrDigit(c))
                {
                    key.Append(char.ToLowerInvariant(c));
                }
            }
            return key.ToString();
        }
    }
}

public static class QuoteText
{
    /// <summary>Longest online quote that still looks good on the widget. Longer ones are skipped.</summary>
    public const int MaxOnlineLength = 280;

    /// <summary>Longest quote the My Quotes list accepts.</summary>
    public const int MaxOwnLength = 400;

    /// <summary>Tidies text from an online source. Returns null if it can't be shown.</summary>
    public static Quote? FromOnline(string? text, string? author, string source)
    {
        var cleanText = Clean(text);
        if (cleanText.Length == 0 || cleanText.Length > MaxOnlineLength)
        {
            return null;
        }
        return new Quote(cleanText, Clean(author), source);
    }

    /// <summary>Decodes HTML entities and collapses runs of whitespace.</summary>
    public static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }
        var decoded = WebUtility.HtmlDecode(value);
        return string.Join(' ', decoded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Font size multiplier that keeps long quotes from dominating the screen.</summary>
    public static double ScaleForLength(int length) => length switch
    {
        <= 110 => 1.0,
        <= 180 => 0.86,
        <= 280 => 0.74,
        _ => 0.64,
    };
}
