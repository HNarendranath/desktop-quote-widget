using System.Net;
using System.Net.Http;
using System.Text.Json;
using WindowsQuotes.Quotes;
using WindowsQuotes.Quotes.Sources;

namespace WindowsQuotes.Tests;

public class SourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void ZenQuotes_parses_real_shape_and_ignores_html_field()
    {
        var quotes = ZenQuotesSource.Parse("""
            [{"q":"Quality is not an act, it is a habit.","a":"Colin R. Davis","c":"37","h":"<blockquote>&ldquo;x&rdquo;</blockquote>"},
             {"q":"Second.","a":"Someone","h":""}]
            """);
        Assert.Equal(2, quotes.Count);
        Assert.Equal(new Quote("Quality is not an act, it is a habit.", "Colin R. Davis", ZenQuotesSource.SourceId), quotes[0]);
    }

    [Fact]
    public void ZenQuotes_rate_limit_message_is_not_treated_as_a_quote()
    {
        var quotes = ZenQuotesSource.Parse("""[{"q":"Too many requests. Obtain an auth key for unlimited access.","a":"zenquotes.io","h":""}]""");
        Assert.Empty(quotes);
    }

    [Fact]
    public void Stoic_parses_real_shape()
    {
        var quote = StoicQuotesSource.Parse("""{"data":{"author":"Seneca","quote":"Begin at once to live."}}""");
        Assert.Equal(new Quote("Begin at once to live.", "Seneca", StoicQuotesSource.SourceId), quote);
    }

    [Fact]
    public void Stoic_blank_author_is_kept_as_empty()
    {
        var quote = StoicQuotesSource.Parse("""{"data":{"author":"","quote":"Don't complain."}}""");
        Assert.Equal(string.Empty, quote!.Author);
    }

    [Fact]
    public void Quotable_parses_real_shape()
    {
        var quotes = QuotableMirrorSource.Parse("""
            {"data":[{"id":"x","content":"In the depth of winter.","tags":[{"id":"y","name":"Wisdom"}],
              "author":{"id":"z","name":"Albert Camus","slug":"albert-camus"}}],
             "metadata":{"total":2107,"page":0}}
            """);
        Assert.Equal(new Quote("In the depth of winter.", "Albert Camus", QuotableMirrorSource.SourceId), Assert.Single(quotes));
    }

    [Fact]
    public void FavQs_parses_real_shape()
    {
        var quote = FavQsSource.Parse("""{"qotd_date":"2026-11-02","quote":{"id":486,"author":"Leonardo da Vinci","body":"Simplicity."}}""");
        Assert.Equal(new Quote("Simplicity.", "Leonardo da Vinci", FavQsSource.SourceId), quote);
    }

    [Theory]
    [InlineData("<html>Bad gateway</html>")]
    [InlineData("")]
    public void Non_json_responses_throw_rather_than_produce_quotes(string body)
    {
        Assert.ThrowsAny<JsonException>(() => ZenQuotesSource.Parse(body));
        Assert.ThrowsAny<JsonException>(() => StoicQuotesSource.Parse(body));
        Assert.ThrowsAny<JsonException>(() => QuotableMirrorSource.Parse(body));
        Assert.ThrowsAny<JsonException>(() => FavQsSource.Parse(body));
    }

    [Fact]
    public void Wrong_shape_json_throws_rather_than_produce_quotes()
    {
        Assert.ThrowsAny<Exception>(() => ZenQuotesSource.Parse("""{"error":"nope"}"""));
        Assert.ThrowsAny<Exception>(() => StoicQuotesSource.Parse("""{"error":"nope"}"""));
        Assert.ThrowsAny<Exception>(() => QuotableMirrorSource.Parse("""{"error":"nope"}"""));
        Assert.ThrowsAny<Exception>(() => FavQsSource.Parse("""{"error":"nope"}"""));
    }

    [Fact]
    public void Cleaning_decodes_entities_collapses_space_and_rejects_bad_lengths()
    {
        Assert.Equal("Art & life", QuoteText.FromOnline("  Art &amp;\n  life ", "A", "s")!.Text);
        Assert.Null(QuoteText.FromOnline("   ", "A", "s"));
        Assert.Null(QuoteText.FromOnline(null, "A", "s"));
        Assert.Null(QuoteText.FromOnline(new string('x', QuoteText.MaxOnlineLength + 1), "A", "s"));
        Assert.Equal(string.Empty, QuoteText.FromOnline("Text", null, "s")!.Author);
    }

    [Fact]
    public void Keys_ignore_case_spacing_and_punctuation()
    {
        Assert.Equal(new Quote("Be kind, always.", "A").Key, new Quote("be  KIND always", "B").Key);
    }

    [Fact]
    public void Merge_skips_duplicates_and_caps_the_cache()
    {
        var cache = Enumerable.Range(0, QuoteFetcher.MaxCachedQuotes).Select(i => new Quote($"Old {i}", "A")).ToList();
        QuoteFetcher.Merge(cache, [new Quote("old 3", "A"), new Quote("Brand new", "A")]);
        Assert.Equal(QuoteFetcher.MaxCachedQuotes, cache.Count);
        Assert.Equal("Brand new", cache[^1].Text);
        Assert.Equal("Old 1", cache[0].Text);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(5, 16)]
    [InlineData(6, 24)]
    [InlineData(50, 24)]
    public void Backoff_doubles_up_to_one_day(int failures, int hours)
    {
        Assert.Equal(TimeSpan.FromHours(hours), QuoteFetcher.Backoff(failures));
    }

    private sealed class FakeSource(string id, Func<IReadOnlyList<Quote>> result) : IQuoteSource
    {
        public string Id => id;
        public string DisplayName => id;
        public TimeSpan MinInterval => TimeSpan.FromHours(6);
        public Task<IReadOnlyList<Quote>> FetchAsync(HttpClient http, CancellationToken cancellationToken) => Task.FromResult(result());
    }

    private static FakeSource Ok(string id) => new(id, () => [new Quote($"From {id}", "A", id)]);

    [Fact]
    public void Never_tried_sources_are_due_even_with_a_full_pool()
    {
        var fetcher = new QuoteFetcher([Ok("a")]);
        Assert.Single(fetcher.DueSources(new HashSet<string>(), new Dictionary<string, SourceStatus>(), 400, Now));
    }

    [Fact]
    public void Disabled_sources_are_never_due()
    {
        var fetcher = new QuoteFetcher([Ok("a")]);
        Assert.Empty(fetcher.DueSources(new HashSet<string> { "a" }, new Dictionary<string, SourceStatus>(), 0, Now));
    }

    [Fact]
    public void Known_sources_wait_for_a_low_pool_and_their_next_attempt_time()
    {
        var fetcher = new QuoteFetcher([Ok("a")]);
        var statuses = new Dictionary<string, SourceStatus>
        {
            ["a"] = new() { LastAttemptAt = Now.AddHours(-1), NextAttemptAt = Now.AddHours(1) },
        };
        Assert.Empty(fetcher.DueSources(new HashSet<string>(), statuses, 0, Now));
        Assert.Empty(fetcher.DueSources(new HashSet<string>(), statuses, QuoteFetcher.LowWaterMark, Now.AddHours(2)));
        Assert.Single(fetcher.DueSources(new HashSet<string>(), statuses, QuoteFetcher.LowWaterMark - 1, Now.AddHours(2)));
    }

    [Fact]
    public async Task One_failing_source_does_not_affect_the_others_and_backs_off()
    {
        var good = Ok("good");
        var bad = new FakeSource("bad", () => throw new HttpRequestException("boom"));
        var fetcher = new QuoteFetcher([good, bad]);
        var statuses = new Dictionary<string, SourceStatus>();

        var quotes = await fetcher.FetchAsync([good, bad], statuses, Now);

        Assert.Equal("From good", Assert.Single(quotes).Text);
        Assert.Null(statuses["good"].LastError);
        Assert.Equal(Now.AddHours(6), statuses["good"].NextAttemptAt);
        Assert.Equal("boom", statuses["bad"].LastError);
        Assert.Equal(1, statuses["bad"].ConsecutiveFailures);
        Assert.Equal(Now.AddHours(1), statuses["bad"].NextAttemptAt);
    }

    [Fact]
    public async Task Success_clears_earlier_failures()
    {
        var source = Ok("a");
        var statuses = new Dictionary<string, SourceStatus>
        {
            ["a"] = new() { ConsecutiveFailures = 4, LastError = "old" },
        };
        await new QuoteFetcher([source]).FetchAsync([source], statuses, Now);
        Assert.Equal(0, statuses["a"].ConsecutiveFailures);
        Assert.Null(statuses["a"].LastError);
    }

    private sealed class StatusHandler(HttpStatusCode code, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "oops")]
    [InlineData(HttpStatusCode.OK, "<html>not json</html>")]
    public async Task Real_sources_record_http_and_parse_failures(HttpStatusCode code, string body)
    {
        var statuses = new Dictionary<string, SourceStatus>();
        var quotes = await new QuoteFetcher(QuoteSourceCatalog.All)
            .FetchAsync(QuoteSourceCatalog.All, statuses, Now, new StatusHandler(code, body));

        Assert.Empty(quotes);
        Assert.Equal(QuoteSourceCatalog.All.Count, statuses.Count);
        Assert.All(statuses.Values, s => Assert.NotNull(s.LastError));
    }
}
