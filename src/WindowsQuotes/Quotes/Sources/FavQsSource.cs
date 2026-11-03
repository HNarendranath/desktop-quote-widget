using System.Net.Http;
using System.Text.Json;

namespace WindowsQuotes.Quotes.Sources;

/// <summary>One featured quote per day.</summary>
public sealed class FavQsSource : IQuoteSource
{
    public const string SourceId = "favqs";
    private const string Url = "https://favqs.com/api/qotd";

    public string Id => SourceId;
    public string DisplayName => "FavQs quote of the day";
    public TimeSpan MinInterval => TimeSpan.FromHours(24);

    public async Task<IReadOnlyList<Quote>> FetchAsync(HttpClient http, CancellationToken cancellationToken)
    {
        var quote = Parse(await http.GetStringAsync(Url, cancellationToken));
        return quote is null ? [] : [quote];
    }

    public static Quote? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var quote = document.RootElement.GetProperty("quote");
        return QuoteText.FromOnline(
            quote.GetProperty("body").GetString(),
            quote.GetProperty("author").GetString(),
            SourceId);
    }
}
