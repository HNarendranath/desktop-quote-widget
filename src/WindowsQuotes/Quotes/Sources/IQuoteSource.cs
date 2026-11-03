using System.Net.Http;

namespace WindowsQuotes.Quotes.Sources;

public interface IQuoteSource
{
    string Id { get; }
    string DisplayName { get; }

    /// <summary>Shortest gap between successful fetches.</summary>
    TimeSpan MinInterval { get; }

    Task<IReadOnlyList<Quote>> FetchAsync(HttpClient http, CancellationToken cancellationToken);
}

public static class QuoteSourceCatalog
{
    public static IReadOnlyList<IQuoteSource> All { get; } =
    [
        new ZenQuotesSource(),
        new StoicQuotesSource(),
        new QuotableMirrorSource(),
        new FavQsSource(),
    ];
}
