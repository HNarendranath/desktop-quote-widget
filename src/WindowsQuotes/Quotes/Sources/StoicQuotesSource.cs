using System.Net.Http;
using System.Text.Json;

namespace WindowsQuotes.Quotes.Sources;

/// <summary>Returns one quote per request, sometimes with a blank author.</summary>
public sealed class StoicQuotesSource : IQuoteSource
{
    public const string SourceId = "stoic";
    private const string Url = "https://stoic.tekloon.net/stoic-quote";
    private const int RequestsPerFetch = 5;

    public string Id => SourceId;
    public string DisplayName => "Stoic quotes";
    public TimeSpan MinInterval => TimeSpan.FromHours(6);

    public async Task<IReadOnlyList<Quote>> FetchAsync(HttpClient http, CancellationToken cancellationToken)
    {
        var quotes = new List<Quote>();
        for (var i = 0; i < RequestsPerFetch; i++)
        {
            if (i > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            if (Parse(await http.GetStringAsync(Url, cancellationToken)) is { } quote)
            {
                quotes.Add(quote);
            }
        }
        return quotes;
    }

    public static Quote? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement.GetProperty("data");
        return QuoteText.FromOnline(
            data.GetProperty("quote").GetString(),
            data.GetProperty("author").GetString(),
            SourceId);
    }
}
