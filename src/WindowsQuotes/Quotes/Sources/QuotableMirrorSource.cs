using System.Net.Http;
using System.Text.Json;

namespace WindowsQuotes.Quotes.Sources;

/// <summary>Community mirror of Quotable, since the original API has gone offline.</summary>
public sealed class QuotableMirrorSource : IQuoteSource
{
    public const string SourceId = "quotable";
    private const int PageSize = 50;

    /// <summary>The mirror held about 2,100 quotes when written, which is about 42 pages of 50.</summary>
    private const int PageCount = 40;

    public string Id => SourceId;
    public string DisplayName => "Quotable (community mirror)";
    public TimeSpan MinInterval => TimeSpan.FromHours(6);

    public async Task<IReadOnlyList<Quote>> FetchAsync(HttpClient http, CancellationToken cancellationToken)
    {
        var page = Random.Shared.Next(PageCount);
        var url = $"https://api.quotable.kurokeita.dev/api/quotes?limit={PageSize}&page={page}";
        return Parse(await http.GetStringAsync(url, cancellationToken));
    }

    public static IReadOnlyList<Quote> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var quotes = new List<Quote>();
        foreach (var item in document.RootElement.GetProperty("data").EnumerateArray())
        {
            var author = item.GetProperty("author").GetProperty("name").GetString();
            if (QuoteText.FromOnline(item.GetProperty("content").GetString(), author, SourceId) is { } quote)
            {
                quotes.Add(quote);
            }
        }
        return quotes;
    }
}
