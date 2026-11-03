using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using WindowsQuotes.Quotes;
using WindowsQuotes.Quotes.Sources;
using WindowsQuotes.Settings;

namespace WindowsQuotes.Widget;

/// <summary>
/// Runs the widget: owns the data, the single timer, the tray icon and the visible window.
/// It sleeps until the next scheduled event, so it uses no CPU in between.
/// </summary>
internal sealed class WidgetHost : IDisposable
{
    private const int DefaultMargin = 48;

    private static readonly TimeSpan StartupFetchDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan MaxTimerDelay = TimeSpan.FromDays(1);

    private readonly DataStore _store;
    private readonly bool _debugFloat;
    private readonly string? _dataDirArgument;
    private readonly QuoteFetcher _fetcher = new(QuoteSourceCatalog.All);
    private readonly IReadOnlyList<Quote> _starter = LoadStarterQuotes();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Normal);
    private readonly HostWindow _host = new();
    private readonly TrayIcon _tray;

    private AppSettings _settings;
    private List<Quote> _own;
    private List<Quote> _cache;
    private WidgetState _state;
    private QuoteRotator _rotator;
    private WidgetWindow _window = null!;
    private bool _moveMode;
    private bool _fetching;

    public WidgetHost(DataStore store, bool debugFloat, string? dataDirArgument)
    {
        _store = store;
        _debugFloat = debugFloat;
        _dataDirArgument = dataDirArgument;
        _settings = store.LoadSettings();
        _own = store.LoadOwnQuotes();
        _cache = store.LoadCache();
        _state = store.LoadState();
        _rotator = new QuoteRotator(_state.Rotation);

        _timer.Tick += (_, _) => OnTimer();
        _tray = new TrayIcon(_host.Handle);

        _host.TrayEvent += OnTrayEvent;
        _host.TaskbarCreated += OnTaskbarCreated;
        _host.ReloadRequested += Reload;
        _host.DisplayChanged += OnDisplayChanged;
        _host.UserReturned += OnTimer;
    }

    private RotationOptions Options => new(TimeSpan.FromMinutes(Math.Max(1, _settings.IntervalMinutes)), _settings.OwnShare);

    private IEnumerable<Quote> EnabledOnline => _cache.Where(q => !_settings.DisabledSources.Contains(q.Source));

    private QuotePools Pools => new(_own, EnabledOnline.ToList(), _starter);

    public void Start()
    {
        ApplyPinRequest();
        _rotator.Tick(Pools, Options, DateTimeOffset.Now);
        SaveState();
        CreateWindow();
        UpdateIndicators();
        Schedule();
        DelayedFirstFetch();
    }

    private async void DelayedFirstFetch()
    {
        await Task.Delay(StartupFetchDelay);
        await FetchIfNeededAsync();
    }

    private void CreateWindow()
    {
        _window = new WidgetWindow(_settings.Look, _settings.Controls, _settings.TextScale, _debugFloat);
        _window.NextRequested += Next;
        _window.HoldMenuRequested += () => NativeMenu.Show(_host.Handle, HoldEntries());
        _window.CopyRequested += Copy;
        _window.FavouriteRequested += SaveCurrentToMyQuotes;
        _window.ContextMenuRequested += () => NativeMenu.Show(_host.Handle, WidgetMenu());
        _window.Moved += SavePosition;
        _window.SizeChanged += (_, _) =>
        {
            if (!_moveMode)
            {
                PlaceWindow(); // A longer quote may have grown it off the screen.
            }
        };
        _window.SetQuote(_state.Rotation.Current, animate: false);
        _window.SetMoveMode(_moveMode);
        _window.Show();
        PlaceWindow();
    }

    private void RecreateWindow()
    {
        var old = _window;
        CreateWindow();
        old.Close();
        UpdateIndicators();
    }

    private void OnTimer()
    {
        if (_host.UserAway)
        {
            return; // Nobody can see it. UserReturned brings us back here.
        }
        if (_rotator.Tick(Pools, Options, DateTimeOffset.Now))
        {
            _window.SetQuote(_state.Rotation.Current, animate: true);
        }
        SaveState();
        UpdateIndicators();
        Schedule();
        _ = FetchIfNeededAsync();
    }

    /// <summary>Sets the one timer for the next event. An indefinite hold needs no timer at all.</summary>
    private void Schedule()
    {
        _timer.Stop();
        if (_rotator.NextWakeAt is not { } next)
        {
            return;
        }
        var delay = next - DateTimeOffset.Now;
        _timer.Interval = delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1)
            : delay > MaxTimerDelay ? MaxTimerDelay
            : delay;
        _timer.Start();
    }

    private void Next()
    {
        if (_rotator.Next(Pools, Options, DateTimeOffset.Now))
        {
            _window.SetQuote(_state.Rotation.Current, animate: true);
        }
        AfterChange();
        _ = FetchIfNeededAsync();
    }

    private void Hold(HoldDuration duration)
    {
        _rotator.Hold(duration, DateTimeOffset.Now);
        AfterChange();
    }

    private void Resume()
    {
        _rotator.Resume(Options, DateTimeOffset.Now);
        AfterChange();
        _ = FetchIfNeededAsync();
    }

    private void AfterChange()
    {
        SaveState();
        UpdateIndicators();
        Schedule();
    }

    private void Copy()
    {
        if (_state.Rotation.Current is not { } quote)
        {
            return;
        }
        var text = quote.Author.Length == 0 ? quote.Text : $"{quote.Text} — {quote.Author}";
        try
        {
            Clipboard.SetDataObject(text, copy: true);
        }
        catch (COMException)
        {
            // Another app has the clipboard open. Copying again will work.
        }
    }

    private void SaveCurrentToMyQuotes()
    {
        if (_state.Rotation.Current is not { } quote)
        {
            return;
        }
        _own = _store.UpdateOwnQuotes(list =>
        {
            if (list.All(q => q.Key != quote.Key))
            {
                list.Add(new Quote(quote.Text, quote.Author, QuoteOrigin.Mine));
            }
        });
        UpdateIndicators();
    }

    private void UpdateIndicators()
    {
        var rotation = _state.Rotation;
        _window.SetHeld(rotation.Held);
        _window.SetSaved(rotation.Current is { } quote && _own.Any(q => q.Key == quote.Key));

        var tooltip = !rotation.Held ? "Quotes"
            : rotation.HeldUntil is { } until ? $"Quotes: held until {FormatEnd(until)}"
            : "Quotes: held until you resume";
        _tray.SetTooltip(tooltip);
    }

    private static string FormatEnd(DateTimeOffset end)
    {
        var local = end.ToLocalTime();
        return local.Date == DateTime.Today ? local.ToString("t")
            : local.Date == DateTime.Today.AddDays(1) ? $"tomorrow {local:t}"
            : local.ToString("ddd d MMM, t");
    }

    private List<MenuEntry> HoldEntries()
    {
        var entries = new List<MenuEntry>();
        if (_state.Rotation.Held)
        {
            entries.Add(new MenuEntry("Resume rotation", Resume));
            entries.Add(MenuEntry.Separator);
        }
        entries.Add(new MenuEntry("For 1 hour", () => Hold(HoldDuration.OneHour)));
        entries.Add(new MenuEntry("For 4 hours", () => Hold(HoldDuration.FourHours)));
        entries.Add(new MenuEntry("Until tomorrow", () => Hold(HoldDuration.UntilTomorrow)));
        entries.Add(new MenuEntry("For 1 week", () => Hold(HoldDuration.OneWeek)));
        entries.Add(new MenuEntry("Until I resume", () => Hold(HoldDuration.Indefinite)));
        return entries;
    }

    private List<MenuEntry> WidgetMenu() =>
    [
        new("Next quote", Next),
        new("Hold this quote", Children: HoldEntries()),
        new("Copy", Copy),
        new("Save to My Quotes", SaveCurrentToMyQuotes),
    ];

    private List<MenuEntry> TrayMenu() =>
    [
        new("Next quote", Next),
        new("Hold this quote", Children: HoldEntries()),
        MenuEntry.Separator,
        new("Add a quote…", () => LaunchManager("--add")),
        new("Manage quotes and settings…", () => LaunchManager()),
        new("Move widget", ToggleMoveMode, Checked: _moveMode),
        MenuEntry.Separator,
        new("Quit", Quit),
    ];

    private void OnTrayEvent(int mouseMessage)
    {
        if (mouseMessage == TrayIcon.WM_RBUTTONUP)
        {
            NativeMenu.Show(_host.Handle, TrayMenu());
        }
        else if (mouseMessage == TrayIcon.WM_LBUTTONUP)
        {
            LaunchManager();
        }
    }

    private void LaunchManager(string? extraArgument = null)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        start.ArgumentList.Add("--manager");
        if (extraArgument is not null)
        {
            start.ArgumentList.Add(extraArgument);
        }
        if (_dataDirArgument is not null)
        {
            start.ArgumentList.Add("--data-dir");
            start.ArgumentList.Add(_dataDirArgument);
        }
        Process.Start(start)?.Dispose();
    }

    private void ToggleMoveMode()
    {
        _moveMode = !_moveMode;
        _window.SetMoveMode(_moveMode);
    }

    private void Quit()
    {
        SaveState();
        Application.Current.Shutdown();
    }

    private void OnTaskbarCreated()
    {
        _tray.Add();
        UpdateIndicators();
        _window.Repin();
    }

    private void OnDisplayChanged()
    {
        _window.Repin();
        PlaceWindow();
    }

    /// <summary>Applies whatever the manager just saved.</summary>
    private void Reload()
    {
        var previous = _settings;
        _settings = _store.LoadSettings();
        _own = _store.LoadOwnQuotes();

        _rotator.Reschedule(Options, DateTimeOffset.Now);
        ApplyPinRequest();

        if (_settings.Look != previous.Look)
        {
            RecreateWindow();
        }
        else
        {
            _window.SetQuote(_state.Rotation.Current, animate: false);
            if (_settings.Controls != previous.Controls)
            {
                _window.SetControls(_settings.Controls);
            }
            if (_settings.TextScale != previous.TextScale)
            {
                _window.SetTextScale(_settings.TextScale);
            }
            PlaceWindow();
        }

        AfterChange();
        _ = FetchIfNeededAsync();
    }

    private void ApplyPinRequest()
    {
        if (_store.TakePinRequest() is { } request)
        {
            _rotator.Pin(request.Quote, request.Duration, DateTimeOffset.Now);
        }
    }

    private async Task FetchIfNeededAsync()
    {
        if (_fetching || _host.UserAway || (_state.Rotation.Held && _state.Rotation.HeldUntil is null))
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var due = _fetcher.DueSources(_settings.DisabledSources, _state.Sources, _rotator.CountUnseen(EnabledOnline), now);
        if (due.Count == 0)
        {
            return;
        }

        _fetching = true;
        try
        {
            // Work on a copy, since the UI thread may save state while this is in flight.
            var statuses = new Dictionary<string, SourceStatus>(_state.Sources);
            var quotes = await _fetcher.FetchAsync(due, statuses, now);
            foreach (var (id, status) in statuses)
            {
                _state.Sources[id] = status;
            }
            QuoteFetcher.Merge(_cache, quotes);
            _store.SaveCache(_cache);
            SaveState();
        }
        catch (IOException ex)
        {
            App.LogError(ex);
        }
        finally
        {
            _fetching = false;
        }
    }

    private void PlaceWindow()
    {
        var monitor = Monitors.Resolve(_settings.Monitor);
        var work = monitor.WorkArea;
        GetWindowRect(_window.Handle, out var rect);
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        var x = work.Right - width - DefaultMargin;
        var y = work.Bottom - height - DefaultMargin;
        if (_settings.Positions.TryGetValue(monitor.Device, out var saved))
        {
            x = work.Left + saved.X;
            y = work.Top + saved.Y;
        }

        // Keep it fully on screen, whatever the layout now is.
        x = Math.Max(work.Left, Math.Min(x, work.Right - width));
        y = Math.Max(work.Top, Math.Min(y, work.Bottom - height));

        if (x != rect.Left || y != rect.Top)
        {
            const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
            SetWindowPos(_window.Handle, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    private void SavePosition()
    {
        if (Monitors.ForWindow(_window.Handle) is not { } monitor)
        {
            return;
        }
        GetWindowRect(_window.Handle, out var rect);
        var position = new WidgetPosition(rect.Left - monitor.WorkArea.Left, rect.Top - monitor.WorkArea.Top);
        _settings = _store.UpdateSettings(settings =>
        {
            settings.Monitor = monitor.Device;
            settings.Positions[monitor.Device] = position;
        });
    }

    private void SaveState()
    {
        try
        {
            _store.SaveState(_state);
        }
        catch (IOException ex)
        {
            App.LogError(ex); // Worth surviving: the rotation still works from memory.
        }
    }

    private static List<Quote> LoadStarterQuotes()
    {
        using var stream = typeof(WidgetHost).Assembly.GetManifestResourceStream("starter-quotes.json")
            ?? throw new InvalidOperationException("Starter quotes are missing from the build.");
        return JsonSerializer.Deserialize(stream, DataJsonContext.Default.ListQuote) ?? [];
    }

    public void Dispose()
    {
        _timer.Stop();
        _tray.Dispose();
        _host.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
