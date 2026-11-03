namespace WindowsQuotes.Quotes;

public enum HoldDuration
{
    OneHour,
    FourHours,
    UntilTomorrow,
    OneWeek,
    Indefinite,
}

public sealed record QuotePools(
    IReadOnlyList<Quote> Own,
    IReadOnlyList<Quote> Online,
    IReadOnlyList<Quote> Starter);

public sealed record RotationOptions(TimeSpan Interval, double OwnShare);

/// <summary>The saved position of the rotation. Survives restarts.</summary>
public sealed class RotationState
{
    public Quote? Current { get; set; }
    public DateTimeOffset? NextChangeAt { get; set; }
    public bool Held { get; set; }

    /// <summary>When a hold ends. Null while held means indefinitely.</summary>
    public DateTimeOffset? HeldUntil { get; set; }

    public HashSet<string> Seen { get; set; } = [];
}

/// <summary>
/// Decides which quote shows and when it changes. Takes the time as an input and has no
/// timers of its own, so the widget can sleep until <see cref="NextWakeAt"/>.
/// </summary>
public sealed class QuoteRotator(RotationState state, Random? random = null, TimeZoneInfo? timeZone = null)
{
    private const int MaxSeenBeforePrune = 1000;

    private readonly Random _random = random ?? Random.Shared;
    private readonly TimeZoneInfo _timeZone = timeZone ?? TimeZoneInfo.Local;

    public RotationState State => state;

    /// <summary>The next moment anything needs to happen, or null if nothing is scheduled.</summary>
    public DateTimeOffset? NextWakeAt => state.Held ? state.HeldUntil : state.NextChangeAt;

    /// <summary>Applies anything now due. Returns true if the displayed quote changed.</summary>
    public bool Tick(QuotePools pools, RotationOptions options, DateTimeOffset now)
    {
        if (state.Held)
        {
            if (state.HeldUntil is not { } until || until > now)
            {
                return false;
            }
            state.Held = false;
            state.HeldUntil = null;
            return Advance(pools, options, now);
        }

        if (state.Current is null || state.NextChangeAt is not { } due || due <= now)
        {
            return Advance(pools, options, now);
        }
        return false;
    }

    /// <summary>Moves to a new quote straight away, ending any hold.</summary>
    public bool Next(QuotePools pools, RotationOptions options, DateTimeOffset now)
    {
        state.Held = false;
        state.HeldUntil = null;
        return Advance(pools, options, now);
    }

    /// <summary>Keeps the current quote on screen. Does nothing if there is no quote yet.</summary>
    public void Hold(HoldDuration duration, DateTimeOffset now)
    {
        if (state.Current is null)
        {
            return;
        }
        state.Held = true;
        state.HeldUntil = EndOfHold(duration, now);
    }

    /// <summary>Shows a specific quote and holds it.</summary>
    public void Pin(Quote quote, HoldDuration duration, DateTimeOffset now)
    {
        state.Current = quote;
        state.Seen.Add(quote.Key);
        Hold(duration, now);
    }

    /// <summary>Ends a hold, leaving the quote up until the next interval.</summary>
    public void Resume(RotationOptions options, DateTimeOffset now)
    {
        state.Held = false;
        state.HeldUntil = null;
        state.NextChangeAt = now + options.Interval;
    }

    /// <summary>Applies a changed interval without ever pushing the next change further away.</summary>
    public void Reschedule(RotationOptions options, DateTimeOffset now)
    {
        var latest = now + options.Interval;
        if (state.NextChangeAt is not { } due || due > latest)
        {
            state.NextChangeAt = latest;
        }
    }

    public DateTimeOffset? EndOfHold(HoldDuration duration, DateTimeOffset now) => duration switch
    {
        HoldDuration.OneHour => now.AddHours(1),
        HoldDuration.FourHours => now.AddHours(4),
        HoldDuration.UntilTomorrow => NextLocalMidnight(now),
        HoldDuration.OneWeek => now.AddDays(7),
        HoldDuration.Indefinite => null,
        _ => throw new ArgumentOutOfRangeException(nameof(duration)),
    };

    /// <summary>How many of these quotes haven't been shown yet.</summary>
    public int CountUnseen(IEnumerable<Quote> quotes) => quotes.Count(q => !state.Seen.Contains(q.Key));

    private DateTimeOffset NextLocalMidnight(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, _timeZone);
        var midnight = DateTime.SpecifyKind(local.Date.AddDays(1), DateTimeKind.Unspecified);
        return new DateTimeOffset(midnight, _timeZone.GetUtcOffset(midnight));
    }

    private bool Advance(QuotePools pools, RotationOptions options, DateTimeOffset now)
    {
        state.NextChangeAt = now + options.Interval;
        var next = Pick(pools, options.OwnShare);
        if (next is null || next == state.Current)
        {
            return false;
        }
        state.Current = next;
        return true;
    }

    private Quote? Pick(QuotePools pools, double ownShare)
    {
        var general = pools.Online.Count > 0 ? pools.Online : pools.Starter;
        var useOwn = pools.Own.Count > 0 && (general.Count == 0 || _random.NextDouble() < ownShare);
        var pool = useOwn ? pools.Own : general;
        if (pool.Count == 0)
        {
            return null;
        }

        if (state.Seen.Count > MaxSeenBeforePrune)
        {
            PruneSeen(pools);
        }

        var currentKey = state.Current?.Key;
        var unseen = pool.Where(q => q.Key != currentKey && !state.Seen.Contains(q.Key)).ToList();
        if (unseen.Count == 0)
        {
            // This pool is used up, so start it again.
            foreach (var quote in pool)
            {
                state.Seen.Remove(quote.Key);
            }
            unseen = pool.Where(q => q.Key != currentKey).ToList();
            if (unseen.Count == 0)
            {
                return pool[0];
            }
        }

        var choice = unseen[_random.Next(unseen.Count)];
        state.Seen.Add(choice.Key);
        return choice;
    }

    private void PruneSeen(QuotePools pools)
    {
        var known = pools.Own.Concat(pools.Online).Concat(pools.Starter).Select(q => q.Key).ToHashSet();
        state.Seen.IntersectWith(known);
    }
}
