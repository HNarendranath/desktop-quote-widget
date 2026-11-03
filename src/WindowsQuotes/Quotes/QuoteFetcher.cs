using System.Net.Http;
using WindowsQuotes.Quotes.Sources;

namespace WindowsQuotes.Quotes;

/// <summary>
/// Decides when online sources are worth calling and merges the results. Each source is
/// isolated: a failing one backs off and never affects the others or the display.
/// </summary>
public sealed class QuoteFetcher(IReadOnlyList<IQuoteSource> sources)
{
    public const int LowWaterMark = 20;
    public const int MaxCachedQuotes = 500;

    private static readonly TimeSpan FirstBackoff = TimeSpan.FromHours(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(24);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    /// <summary>The sources worth calling now, given how many unseen quotes remain.</summary>
    public IReadOnlyList<IQuoteSource> DueSources(
        ISet<string> disabled,
        IReadOnlyDictionary<string, SourceStatus> statuses,
        int unseenOnline,
        DateTimeOffset now)
    {
        return sources.Where(source =>
        {
            if (disabled.Contains(source.Id))
            {
                return false;
            }
            statuses.TryGetValue(source.Id, out var status);
            if (status?.LastAttemptAt is null)
            {
                return true; // Newly enabled, so try it once whatever the pool size.
            }
            return unseenOnline < LowWaterMark && (status.NextAttemptAt is not { } next || next <= now);
        }).ToList();
    }

    /// <summary>Calls the given sources, recording each outcome, and returns all new quotes.</summary>
    public async Task<List<Quote>> FetchAsync(
        IReadOnlyList<IQuoteSource> due,
        IDictionary<string, SourceStatus> statuses,
        DateTimeOffset now,
        HttpMessageHandler? handler = null,
        CancellationToken cancellationToken = default)
    {
        // Created per run and disposed, so nothing network-related stays in memory.
        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = RequestTimeout;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WindowsQuotes/1.0");

        var results = await Task.WhenAll(due.Select(source => FetchOneAsync(source, http, statuses, now, cancellationToken)));
        return results.SelectMany(r => r).ToList();
    }

    /// <summary>Doubles from one hour up to one day.</summary>
    public static TimeSpan Backoff(int failures)
    {
        var hours = FirstBackoff.TotalHours * Math.Pow(2, Math.Max(0, failures - 1));
        return hours >= MaxBackoff.TotalHours ? MaxBackoff : TimeSpan.FromHours(hours);
    }

    /// <summary>Adds new quotes, skipping duplicates and dropping the oldest above the cap.</summary>
    public static void Merge(List<Quote> cache, IEnumerable<Quote> incoming)
    {
        var known = cache.Select(q => q.Key).ToHashSet();
        foreach (var quote in incoming)
        {
            if (known.Add(quote.Key))
            {
                cache.Add(quote);
            }
        }
        if (cache.Count > MaxCachedQuotes)
        {
            cache.RemoveRange(0, cache.Count - MaxCachedQuotes);
        }
    }

    private static async Task<IReadOnlyList<Quote>> FetchOneAsync(
        IQuoteSource source,
        HttpClient http,
        IDictionary<string, SourceStatus> statuses,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Quote>? quotes = null;
        string? error = null;
        try
        {
            quotes = await source.FetchAsync(http, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            error = ex.Message;
        }

        lock (statuses)
        {
            if (!statuses.TryGetValue(source.Id, out var status))
            {
                status = new SourceStatus();
                statuses[source.Id] = status;
            }
            status.LastAttemptAt = now;
            if (error is null)
            {
                status.LastSuccessAt = now;
                status.ConsecutiveFailures = 0;
                status.LastError = null;
                status.NextAttemptAt = now + source.MinInterval;
            }
            else
            {
                status.ConsecutiveFailures++;
                status.LastError = error;
                status.NextAttemptAt = now + Backoff(status.ConsecutiveFailures);
            }
        }
        return quotes ?? [];
    }
}
