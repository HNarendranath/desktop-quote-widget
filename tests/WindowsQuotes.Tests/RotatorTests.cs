using WindowsQuotes.Quotes;

namespace WindowsQuotes.Tests;

public class RotatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 14, 30, 0, TimeSpan.Zero);
    private static readonly RotationOptions Options = new(TimeSpan.FromHours(1), 0.5);

    private static List<Quote> Make(string prefix, int count, string source = "web") =>
        Enumerable.Range(0, count).Select(i => new Quote($"{prefix} {(char)('a' + i)}", "Author", source)).ToList();

    private static QuoteRotator Rotator(RotationState? state = null, int seed = 1) =>
        new(state ?? new RotationState(), new Random(seed), TimeZoneInfo.Utc);

    private static QuotePools Pools(List<Quote>? own = null, List<Quote>? online = null, List<Quote>? starter = null) =>
        new(own ?? [], online ?? [], starter ?? []);

    [Fact]
    public void Shows_every_quote_once_before_repeating()
    {
        var quotes = Make("online", 10);
        var rotator = Rotator();
        var shown = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            rotator.Next(Pools(online: quotes), Options, Now);
            shown.Add(rotator.State.Current!.Text);
        }
        Assert.Equal(10, shown.Distinct().Count());
    }

    [Fact]
    public void Never_repeats_back_to_back_when_a_pool_restarts()
    {
        var quotes = Make("online", 3);
        var rotator = Rotator();
        string? previous = null;
        for (var i = 0; i < 60; i++)
        {
            rotator.Next(Pools(online: quotes), Options, Now);
            Assert.NotEqual(previous, rotator.State.Current!.Text);
            previous = rotator.State.Current!.Text;
        }
    }

    [Theory]
    [InlineData(0.0, 0, 0)]
    [InlineData(1.0, 1000, 1000)]
    [InlineData(0.5, 430, 570)]
    public void Own_share_controls_how_often_own_quotes_show(double share, int min, int max)
    {
        var rotator = Rotator();
        var pools = Pools(own: Make("own", 5, QuoteOrigin.Mine), online: Make("online", 5));
        var options = Options with { OwnShare = share };
        var own = 0;
        for (var i = 0; i < 1000; i++)
        {
            rotator.Next(pools, options, Now);
            if (rotator.State.Current!.Source == QuoteOrigin.Mine)
            {
                own++;
            }
        }
        Assert.InRange(own, min, max);
    }

    [Fact]
    public void Falls_back_to_starter_quotes_when_nothing_else_exists()
    {
        var rotator = Rotator();
        rotator.Tick(Pools(starter: Make("starter", 3, QuoteOrigin.Starter)), Options, Now);
        Assert.Equal(QuoteOrigin.Starter, rotator.State.Current!.Source);
    }

    [Fact]
    public void Own_quotes_show_even_at_zero_share_when_there_is_nothing_else()
    {
        var rotator = Rotator();
        rotator.Tick(Pools(own: Make("own", 2, QuoteOrigin.Mine)), Options with { OwnShare = 0 }, Now);
        Assert.Equal(QuoteOrigin.Mine, rotator.State.Current!.Source);
    }

    [Fact]
    public void Prefers_online_quotes_over_starter_quotes()
    {
        var rotator = Rotator();
        rotator.Tick(Pools(online: Make("online", 2), starter: Make("starter", 2, QuoteOrigin.Starter)), Options, Now);
        Assert.Equal("web", rotator.State.Current!.Source);
    }

    [Fact]
    public void No_quotes_at_all_leaves_nothing_showing()
    {
        var rotator = Rotator();
        Assert.False(rotator.Tick(Pools(), Options, Now));
        Assert.Null(rotator.State.Current);
    }

    [Fact]
    public void Single_quote_pool_stays_stable()
    {
        var rotator = Rotator();
        var pools = Pools(own: Make("own", 1, QuoteOrigin.Mine));
        rotator.Tick(pools, Options, Now);
        var first = rotator.State.Current;
        rotator.Next(pools, Options, Now.AddHours(1));
        Assert.Equal(first, rotator.State.Current);
    }

    [Fact]
    public void Tick_changes_only_when_due()
    {
        var pools = Pools(online: Make("online", 5));
        var rotator = Rotator();
        Assert.True(rotator.Tick(pools, Options, Now));
        var first = rotator.State.Current;

        Assert.False(rotator.Tick(pools, Options, Now.AddMinutes(59)));
        Assert.Equal(first, rotator.State.Current);

        Assert.True(rotator.Tick(pools, Options, Now.AddMinutes(61)));
        Assert.NotEqual(first, rotator.State.Current);
        Assert.Equal(Now.AddMinutes(121), rotator.NextWakeAt);
    }

    [Theory]
    [InlineData(HoldDuration.OneHour, 1)]
    [InlineData(HoldDuration.FourHours, 4)]
    [InlineData(HoldDuration.OneWeek, 168)]
    public void Timed_hold_keeps_the_quote_until_it_expires(HoldDuration duration, int hours)
    {
        var pools = Pools(online: Make("online", 5));
        var rotator = Rotator();
        rotator.Tick(pools, Options, Now);
        var held = rotator.State.Current;
        rotator.Hold(duration, Now);

        Assert.Equal(Now.AddHours(hours), rotator.NextWakeAt);
        Assert.False(rotator.Tick(pools, Options, Now.AddHours(hours).AddSeconds(-1)));
        Assert.Equal(held, rotator.State.Current);

        Assert.True(rotator.Tick(pools, Options, Now.AddHours(hours)));
        Assert.NotEqual(held, rotator.State.Current);
        Assert.False(rotator.State.Held);
    }

    [Fact]
    public void Until_tomorrow_ends_at_local_midnight()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(5), "Test", "Test");
        var rotator = new QuoteRotator(new RotationState(), new Random(1), zone);

        // 14:30 UTC is 19:30 in UTC+5, so the next local midnight is 19:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 3, 10, 19, 0, 0, TimeSpan.Zero), rotator.EndOfHold(HoldDuration.UntilTomorrow, Now));
    }

    [Fact]
    public void Indefinite_hold_never_expires_and_schedules_nothing()
    {
        var pools = Pools(online: Make("online", 5));
        var rotator = Rotator();
        rotator.Tick(pools, Options, Now);
        var held = rotator.State.Current;
        rotator.Hold(HoldDuration.Indefinite, Now);

        Assert.Null(rotator.NextWakeAt);
        Assert.False(rotator.Tick(pools, Options, Now.AddYears(5)));
        Assert.Equal(held, rotator.State.Current);
    }

    [Fact]
    public void Hold_that_expired_while_the_app_was_off_ends_at_next_launch()
    {
        var pools = Pools(online: Make("online", 5));
        var state = new RotationState();
        var first = Rotator(state);
        first.Tick(pools, Options, Now);
        var held = state.Current;
        first.Hold(HoldDuration.OneHour, Now);

        var afterRestart = Rotator(state, seed: 2);
        Assert.True(afterRestart.Tick(pools, Options, Now.AddDays(3)));
        Assert.False(state.Held);
        Assert.NotEqual(held, state.Current);
    }

    [Fact]
    public void Next_ends_a_hold_and_moves_on()
    {
        var pools = Pools(online: Make("online", 5));
        var rotator = Rotator();
        rotator.Tick(pools, Options, Now);
        var held = rotator.State.Current;
        rotator.Hold(HoldDuration.Indefinite, Now);

        Assert.True(rotator.Next(pools, Options, Now));
        Assert.False(rotator.State.Held);
        Assert.NotEqual(held, rotator.State.Current);
    }

    [Fact]
    public void Resume_keeps_the_quote_until_the_next_interval()
    {
        var pools = Pools(online: Make("online", 5));
        var rotator = Rotator();
        rotator.Tick(pools, Options, Now);
        var held = rotator.State.Current;
        rotator.Hold(HoldDuration.Indefinite, Now);

        rotator.Resume(Options, Now.AddDays(1));

        Assert.False(rotator.State.Held);
        Assert.Equal(held, rotator.State.Current);
        Assert.Equal(Now.AddDays(1).AddHours(1), rotator.NextWakeAt);
    }

    [Fact]
    public void Pin_shows_a_specific_quote_and_holds_it()
    {
        var pools = Pools(online: Make("online", 5));
        var rotator = Rotator();
        rotator.Tick(pools, Options, Now);
        var mine = new Quote("Mine", "Me");

        rotator.Pin(mine, HoldDuration.FourHours, Now);

        Assert.Equal(mine, rotator.State.Current);
        Assert.True(rotator.State.Held);
        Assert.False(rotator.Tick(pools, Options, Now.AddHours(3)));
        Assert.Equal(mine, rotator.State.Current);
    }

    [Fact]
    public void Shorter_interval_brings_the_next_change_forward_but_longer_does_not_delay_it()
    {
        var pools = Pools(online: Make("online", 5));
        var rotator = Rotator();
        rotator.Tick(pools, Options, Now);

        rotator.Reschedule(Options with { Interval = TimeSpan.FromHours(4) }, Now);
        Assert.Equal(Now.AddHours(1), rotator.NextWakeAt);

        rotator.Reschedule(Options with { Interval = TimeSpan.FromMinutes(15) }, Now);
        Assert.Equal(Now.AddMinutes(15), rotator.NextWakeAt);
    }

    [Fact]
    public void Seen_list_stays_bounded()
    {
        var state = new RotationState();
        state.Seen.UnionWith(Enumerable.Range(0, 5000).Select(i => $"gone{i}"));
        var rotator = Rotator(state);
        rotator.Tick(Pools(online: Make("online", 5)), Options, Now);
        Assert.True(state.Seen.Count <= 5);
    }
}
