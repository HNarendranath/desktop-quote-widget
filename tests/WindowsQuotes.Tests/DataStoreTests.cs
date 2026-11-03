using System.IO;
using WindowsQuotes.Quotes;
using WindowsQuotes.Settings;

namespace WindowsQuotes.Tests;

public sealed class DataStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "wq-tests-" + Guid.NewGuid().ToString("N"));
    private readonly DataStore _store;

    public DataStoreTests() => _store = new DataStore(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Missing_files_give_defaults()
    {
        Assert.Equal(60, _store.LoadSettings().IntervalMinutes);
        Assert.Empty(_store.LoadOwnQuotes());
        Assert.Empty(_store.LoadCache());
        Assert.Null(_store.LoadState().Rotation.Current);
        Assert.Null(_store.TakePinRequest());
    }

    [Fact]
    public void Settings_round_trip()
    {
        _store.UpdateSettings(settings =>
        {
            settings.Look = WidgetLook.Frosted;
            settings.Controls = WidgetControls.TrayOnly;
            settings.IntervalMinutes = 240;
            settings.OwnShare = 0.25;
            settings.Monitor = @"\\.\DISPLAY2";
            settings.Positions[@"\\.\DISPLAY2"] = new WidgetPosition(10, 20);
            settings.DisabledSources.Add("favqs");
        });

        var loaded = _store.LoadSettings();
        Assert.Equal(WidgetLook.Frosted, loaded.Look);
        Assert.Equal(WidgetControls.TrayOnly, loaded.Controls);
        Assert.Equal(240, loaded.IntervalMinutes);
        Assert.Equal(0.25, loaded.OwnShare);
        Assert.Equal(new WidgetPosition(10, 20), loaded.Positions[@"\\.\DISPLAY2"]);
        Assert.Contains("favqs", loaded.DisabledSources);
    }

    [Fact]
    public void Hold_state_round_trips()
    {
        var until = new DateTimeOffset(2026, 3, 10, 14, 30, 0, TimeSpan.FromHours(1));
        var state = new WidgetState();
        state.Rotation.Current = new Quote("Text", "Author", "zenquotes");
        state.Rotation.Held = true;
        state.Rotation.HeldUntil = until;
        state.Rotation.Seen.Add("text");
        state.Sources["zenquotes"] = new SourceStatus { ConsecutiveFailures = 2, LastError = "boom" };
        _store.SaveState(state);

        var loaded = _store.LoadState();
        Assert.Equal(state.Rotation.Current, loaded.Rotation.Current);
        Assert.True(loaded.Rotation.Held);
        Assert.Equal(until, loaded.Rotation.HeldUntil);
        Assert.Contains("text", loaded.Rotation.Seen);
        Assert.Equal(2, loaded.Sources["zenquotes"].ConsecutiveFailures);
    }

    [Fact]
    public void Own_quotes_update_keeps_existing_entries()
    {
        _store.UpdateOwnQuotes(q => q.Add(new Quote("One", "A")));
        _store.UpdateOwnQuotes(q => q.Add(new Quote("Two", "B")));
        Assert.Equal(["One", "Two"], _store.LoadOwnQuotes().Select(q => q.Text));
    }

    [Fact]
    public void Pin_request_is_consumed_once()
    {
        var request = new PinRequest(new Quote("One", "A"), HoldDuration.OneWeek);
        _store.RequestPin(request);
        Assert.Equal(request, _store.TakePinRequest());
        Assert.Null(_store.TakePinRequest());
    }

    [Theory]
    [InlineData("settings.json")]
    [InlineData("my-quotes.json")]
    [InlineData("cache.json")]
    [InlineData("state.json")]
    public void Corrupt_file_is_set_aside_and_defaults_are_used(string file)
    {
        var path = Path.Combine(_directory, file);
        File.WriteAllText(path, "{ this is not json");

        Assert.Equal(60, _store.LoadSettings().IntervalMinutes);
        Assert.Empty(_store.LoadOwnQuotes());
        Assert.Empty(_store.LoadCache());
        Assert.Null(_store.LoadState().Rotation.Current);

        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".corrupt"));
    }

    [Fact]
    public void Save_leaves_no_temp_files()
    {
        _store.SaveCache([new Quote("One", "A")]);
        Assert.DoesNotContain(Directory.GetFiles(_directory), f => f.EndsWith(".tmp"));
    }

    [Fact]
    public async Task Concurrent_updates_lose_nothing()
    {
        var tasks = Enumerable.Range(0, 40).Select(i => Task.Run(() =>
            new DataStore(_directory).UpdateOwnQuotes(q => q.Add(new Quote($"Quote {i}", "A")))));
        await Task.WhenAll(tasks);
        Assert.Equal(40, _store.LoadOwnQuotes().Count);
    }

    [Fact]
    public void Starter_quotes_asset_is_valid_and_has_no_duplicates()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "starter-quotes.sample.json");
        File.Copy(path, Path.Combine(_directory, "cache.json"));
        var quotes = _store.LoadCache();
        Assert.True(quotes.Count >= 15);
        Assert.Equal(quotes.Count, quotes.Select(q => q.Key).Distinct().Count());
        Assert.All(quotes, q =>
        {
            Assert.Equal(QuoteOrigin.Starter, q.Source);
            Assert.False(string.IsNullOrWhiteSpace(q.Author));
            Assert.InRange(q.Text.Length, 1, QuoteText.MaxOnlineLength);
        });
    }
}
