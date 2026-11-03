using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Windows.Threading;
using WindowsQuotes.Quotes;
using WindowsQuotes.Quotes.Sources;
using WindowsQuotes.Settings;
using WindowsQuotes.Widget;

namespace WindowsQuotes.Manager;

/// <summary>
/// Adds and edits quotes and changes settings. It runs as its own short-lived process, so
/// the widget doesn't carry its memory. Every change saves immediately and tells the widget.
/// </summary>
public partial class ManagerWindow : Window
{
    private readonly DataStore _store;
    private readonly DispatcherTimer _sliderDebounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private List<Quote> _quotes;
    private Quote? _editing;
    private bool _loading = true;

    public ManagerWindow(DataStore store, bool focusAddQuote)
    {
        _store = store;
        _quotes = store.LoadOwnQuotes();
        InitializeComponent();

        // The widget's star and lock buttons can change things while this window is open.
        // Clicking the widget doesn't deactivate this window, so moving back to it counts too.
        Activated += (_, _) =>
        {
            ReloadQuotes();
            ReloadLockState();
        };
        MouseEnter += (_, _) => ReloadLockState();

        _sliderDebounce.Tick += (_, _) =>
        {
            _sliderDebounce.Stop();
            SaveSliders();
        };

        LoadSettingsIntoControls();
        RefreshQuoteList();
        RefreshEditor();
        _loading = false;

        if (focusAddQuote)
        {
            Loaded += (_, _) => QuoteInput.Focus();
        }
    }

    private static void NotifyWidget() => HostWindow.NotifyReload();

    private bool ShowingStarred => QuoteViews.SelectedItem == StarredView;

    private static bool IsStarred(Quote quote) => quote.Source == QuoteOrigin.Starred;

    private void ReloadLockState()
    {
        if (!_loading)
        {
            LockPositionBox.IsChecked = _store.LoadSettings().PositionLocked;
        }
    }

    private void ReloadQuotes()
    {
        if (_loading)
        {
            return;
        }
        var selected = _editing;
        _quotes = _store.LoadOwnQuotes();
        RefreshQuoteList();
        if (selected is not null && !_quotes.Contains(selected))
        {
            ClearEditor();
        }
    }

    private void RefreshQuoteList()
    {
        var selected = QuoteList.SelectedItem;
        var inView = _quotes.Where(q => IsStarred(q) == ShowingStarred).ToList();
        var search = SearchBox.Text.Trim();
        QuoteList.ItemsSource = search.Length == 0
            ? inView
            : inView.Where(q =>
                q.Text.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || q.Author.Contains(search, StringComparison.CurrentCultureIgnoreCase)).ToList();
        QuoteList.SelectedItem = selected;

        AddedView.Header = $"Added by you ({_quotes.Count(q => !IsStarred(q))})";
        StarredView.Header = $"Starred ({_quotes.Count(IsStarred)})";

        EmptyMessage.Text = inView.Count > 0 ? "No quotes match your search."
            : ShowingStarred ? "No starred quotes yet. Hover over the quote on your desktop and click the star."
            : "No quotes yet. Add your first one below.";
        EmptyMessage.Visibility = QuoteList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnQuoteViewChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, QuoteViews) || SearchBox is null)
        {
            return; // Still being constructed, or an event from inside the tab.
        }
        ClearEditor();
        RefreshQuoteList();
        EditorMessage.Text = string.Empty;
    }

    private void RefreshEditor()
    {
        var length = QuoteInput.Text.Trim().Length;
        var editing = _editing is not null;
        var starred = ShowingStarred;

        // Starred quotes come from elsewhere, so they can be removed but not reworded.
        EditorFields.Visibility = starred ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.Visibility = starred ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.Visibility = starred ? Visibility.Collapsed : Visibility.Visible;
        DeleteButton.Content = starred ? "Remove star" : "Delete";

        SaveButton.Content = editing ? "Save changes" : "Add quote";
        SaveButton.IsEnabled = length is > 0 and <= QuoteText.MaxOwnLength;
        DeleteButton.IsEnabled = editing;
        PinButton.IsEnabled = editing;
        EditorMessage.Text = length > QuoteText.MaxOwnLength
            ? $"Too long by {length - QuoteText.MaxOwnLength} characters. The limit is {QuoteText.MaxOwnLength}."
            : string.Empty;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => RefreshQuoteList();

    private void OnEditorChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading)
        {
            RefreshEditor();
        }
    }

    private void OnQuoteSelected(object sender, SelectionChangedEventArgs e)
    {
        if (QuoteList.SelectedItem is not Quote quote)
        {
            return;
        }
        _editing = quote;
        QuoteInput.Text = quote.Text;
        AuthorInput.Text = quote.Author;
        RefreshEditor();
    }

    private void ClearEditor()
    {
        _editing = null;
        if (QuoteList is not null)
        {
            QuoteList.SelectedItem = null;
        }
        QuoteInput.Clear();
        AuthorInput.Clear();
        RefreshEditor();
    }

    private void OnClearEditor(object sender, RoutedEventArgs e) => ClearEditor();

    private void OnSaveQuote(object sender, RoutedEventArgs e)
    {
        var quote = new Quote(QuoteText.Clean(QuoteInput.Text), QuoteText.Clean(AuthorInput.Text));
        var editing = _editing;
        var duplicate = false;

        _quotes = _store.UpdateOwnQuotes(list =>
        {
            duplicate = list.Any(q => q.Key == quote.Key && q != editing);
            if (duplicate)
            {
                return;
            }
            var index = editing is null ? -1 : list.IndexOf(editing);
            if (index >= 0)
            {
                list[index] = quote;
            }
            else
            {
                list.Add(quote);
            }
        });

        if (duplicate)
        {
            EditorMessage.Text = "That quote is already in your added or starred quotes.";
            return;
        }

        ClearEditor();
        RefreshQuoteList();
        EditorMessage.Text = "Saved.";
        QuoteInput.Focus();
        NotifyWidget();
    }

    private void OnDeleteQuote(object sender, RoutedEventArgs e)
    {
        if (_editing is not { } target)
        {
            return;
        }
        _quotes = _store.UpdateOwnQuotes(list => list.Remove(target));
        ClearEditor();
        RefreshQuoteList();
        NotifyWidget();
    }

    private void OnPinQuote(object sender, RoutedEventArgs e)
    {
        if (_editing is not { } quote)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = PinButton };
        foreach (var (label, duration) in new[]
        {
            ("For 1 hour", HoldDuration.OneHour),
            ("For 4 hours", HoldDuration.FourHours),
            ("Until tomorrow", HoldDuration.UntilTomorrow),
            ("For 1 week", HoldDuration.OneWeek),
            ("Until I resume", HoldDuration.Indefinite),
        })
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) =>
            {
                _store.RequestPin(new PinRequest(quote, duration));
                NotifyWidget();
                EditorMessage.Text = "Now showing on your desktop.";
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void LoadSettingsIntoControls()
    {
        var settings = _store.LoadSettings();

        LookBox.ItemsSource = new[]
        {
            new Choice<WidgetLook>("Bare text on your wallpaper", WidgetLook.BareText),
            new Choice<WidgetLook>("Frosted glass card", WidgetLook.Frosted),
            new Choice<WidgetLook>("Tinted dark card", WidgetLook.Tinted),
        };
        Select(LookBox, settings.Look);

        var fonts = QuoteFonts.Available();
        FontBox.ItemsSource = fonts;
        FontBox.SelectedItem = fonts.FirstOrDefault(f => f.Name == settings.Font)
            ?? fonts.FirstOrDefault(f => f.Name == QuoteFonts.DefaultName)
            ?? fonts[0];
        LowercaseBox.IsChecked = settings.Lowercase;
        ShowAuthorsBox.IsChecked = settings.ShowAuthors;
        LockPositionBox.IsChecked = settings.PositionLocked;

        ControlsBox.ItemsSource = new[]
        {
            new Choice<WidgetControls>("Buttons appear on hover", WidgetControls.HoverButtons),
            new Choice<WidgetControls>("Right-click menu", WidgetControls.RightClickMenu),
            new Choice<WidgetControls>("Tray icon only (clicks pass through)", WidgetControls.TrayOnly),
        };
        Select(ControlsBox, settings.Controls);

        IntervalBox.ItemsSource = AppSettings.IntervalPresets
            .Append(settings.IntervalMinutes)
            .Distinct()
            .Order()
            .Select(minutes => new Choice<int>(DescribeInterval(minutes), minutes))
            .ToList();
        Select(IntervalBox, settings.IntervalMinutes);

        var monitors = Monitors.All().OrderByDescending(m => m.IsPrimary).ToList();
        MonitorBox.ItemsSource = monitors
            .Select((m, i) => new Choice<string?>(
                $"Display {i + 1}{(m.IsPrimary ? " (primary)" : string.Empty)}, {m.Bounds.Width} × {m.Bounds.Height}",
                m.Device))
            .ToList();
        Select(MonitorBox, Monitors.Resolve(settings.Monitor).Device);

        OwnShareSlider.Value = Math.Round(settings.OwnShare * 100);
        TextScaleSlider.Value = Math.Round(settings.TextScale * 100);
        UpdateSliderLabels();

        StartupBox.IsChecked = StartWithWindows.IsEnabled;
    }

    private static string DescribeInterval(int minutes) => minutes switch
    {
        1440 => "Day",
        >= 60 when minutes % 60 == 0 => minutes == 60 ? "Hour" : $"{minutes / 60} hours",
        _ => $"{minutes} minutes",
    };

    private static void Select<T>(ComboBox box, T value)
    {
        box.SelectedItem = box.ItemsSource?.Cast<Choice<T>>().FirstOrDefault(c => EqualityComparer<T>.Default.Equals(c.Value, value));
    }

    private void OnSettingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var look = ((Choice<WidgetLook>)LookBox.SelectedItem).Value;
        var font = ((QuoteFont)FontBox.SelectedItem).Name;
        var controls = ((Choice<WidgetControls>)ControlsBox.SelectedItem).Value;
        var interval = ((Choice<int>)IntervalBox.SelectedItem).Value;
        var monitor = ((Choice<string?>)MonitorBox.SelectedItem).Value;
        _store.UpdateSettings(settings =>
        {
            settings.Look = look;
            settings.Font = font;
            settings.Controls = controls;
            settings.IntervalMinutes = interval;
            settings.Monitor = monitor;
        });
        NotifyWidget();
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateSliderLabels();
        if (!_loading)
        {
            _sliderDebounce.Stop();
            _sliderDebounce.Start();
        }
    }

    private void UpdateSliderLabels()
    {
        if (OwnShareLabel is null || TextScaleLabel is null)
        {
            return; // Still being constructed.
        }
        OwnShareLabel.Text = $"Show your added and starred quotes about {OwnShareSlider.Value:0}% of the time";
        TextScaleLabel.Text = $"Text size: {TextScaleSlider.Value:0}%";
    }

    private void SaveSliders()
    {
        var ownShare = OwnShareSlider.Value / 100;
        var textScale = TextScaleSlider.Value / 100;
        _store.UpdateSettings(settings =>
        {
            settings.OwnShare = ownShare;
            settings.TextScale = textScale;
        });
        NotifyWidget();
    }

    private void OnResetPosition(object sender, RoutedEventArgs e)
    {
        var monitor = ((Choice<string?>)MonitorBox.SelectedItem).Value;
        _store.UpdateSettings(settings => settings.Centers.Remove(monitor ?? string.Empty));
        NotifyWidget();
    }

    private void OnDisplayOptionClicked(object sender, RoutedEventArgs e)
    {
        var lowercase = LowercaseBox.IsChecked == true;
        var showAuthors = ShowAuthorsBox.IsChecked == true;
        _store.UpdateSettings(settings =>
        {
            settings.Lowercase = lowercase;
            settings.ShowAuthors = showAuthors;
        });
        NotifyWidget();
    }

    private void OnLockPositionClicked(object sender, RoutedEventArgs e)
    {
        var locked = LockPositionBox.IsChecked == true;
        _store.UpdateSettings(settings => settings.PositionLocked = locked);
        NotifyWidget();
    }

    private void OnStartupClicked(object sender, RoutedEventArgs e)
    {
        StartWithWindows.IsEnabled = StartupBox.IsChecked == true;
    }

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Tabs) && Tabs.SelectedItem == SourcesTab)
        {
            RefreshSources();
        }
    }

    private void RefreshSources()
    {
        var disabled = _store.LoadSettings().DisabledSources;
        var statuses = _store.LoadState().Sources;

        SourceList.Children.Clear();
        foreach (var source in QuoteSourceCatalog.All)
        {
            statuses.TryGetValue(source.Id, out var status);

            var toggle = new CheckBox { Content = source.DisplayName, IsChecked = !disabled.Contains(source.Id) };
            var id = source.Id;
            toggle.Click += (_, _) =>
            {
                var enabled = toggle.IsChecked == true;
                _store.UpdateSettings(settings =>
                {
                    if (enabled)
                    {
                        settings.DisabledSources.Remove(id);
                    }
                    else
                    {
                        settings.DisabledSources.Add(id);
                    }
                });
                NotifyWidget();
            };

            SourceList.Children.Add(toggle);
            SourceList.Children.Add(new TextBlock
            {
                Text = DescribeStatus(status),
                Opacity = 0.65,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(28, 0, 0, 12),
            });
        }
    }

    private static string DescribeStatus(SourceStatus? status)
    {
        if (status?.LastAttemptAt is null)
        {
            return "Not fetched yet.";
        }
        if (status.LastError is { } error)
        {
            var when = status.NextAttemptAt is { } next ? $" Will try again {next.ToLocalTime():ddd HH:mm}." : string.Empty;
            return $"Last attempt failed: {error}{when}";
        }
        return status.LastSuccessAt is { } success ? $"Last fetched {success.ToLocalTime():ddd d MMM, HH:mm}." : "Not fetched yet.";
    }

    private void OnOpenLink(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        e.Handled = true;
    }

    private sealed record Choice<T>(string Label, T Value)
    {
        public override string ToString() => Label;
    }
}
