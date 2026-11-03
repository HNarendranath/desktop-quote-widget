using System.Net.Http;
using System.Text.Json;

namespace WindowsQuotes.Quotes.Sources;

/// <summary>Free tier: 5 requests per 30 seconds, and attribution with a link is required.</summary>
public sealed class ZenQuotesSource : IQuoteSource
{
    public const string SourceId = "zenquotes";
    private const string Url = "https://zenquotes.io/api/quotes";

    public string Id => SourceId;
    public string DisplayName => "ZenQuotes";
    public TimeSpan MinInterval => TimeSpan.FromHours(6);

    public async Task<IReadOnlyList<Quote>> FetchAsync(HttpClient http, CancellationToken cancellationToken) =>
        Parse(await http.GetStringAsync(Url, cancellationToken));

    public static IReadOnlyList<Quote> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var quotes = new List<Quote>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var author = item.GetProperty("a").GetString();

            // When rate limited, the API returns its error message as a quote by "zenquotes.io".
            if (string.Equals(author, "zenquotes.io", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (QuoteText.FromOnline(item.GetProperty("q").GetString(), author, SourceId) is { } quote)
            {
                quotes.Add(quote);
            }
        }
        return quotes;
    }
}
