using System.IO;
using System.IO.Packaging;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WindowsQuotes.Manager;
using WindowsQuotes.Quotes;
using WindowsQuotes.Widget;

namespace WindowsQuotes.Tests;

/// <summary>The manager's font and lowercase settings. The window is built but never shown.</summary>
public sealed class StyleSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "wq-style-" + Guid.NewGuid().ToString("N"));

    public StyleSettingsTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static void OnUiThread(Action test)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = PackUriHelper.UriSchemePack;
                test();
            }
            catch (Exception e)
            {
                failure = ExceptionDispatchInfo.Capture(e);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    [Fact]
    public void Older_settings_files_get_the_default_font_and_normal_case()
    {
        File.WriteAllText(Path.Combine(_directory, "settings.json"), """{"look":"Frosted","textScale":1.2}""");
        var settings = new DataStore(_directory).LoadSettings();
        Assert.Equal(QuoteFonts.DefaultName, settings.Font);
        Assert.False(settings.Lowercase);
        Assert.True(settings.ShowAuthors);
        Assert.False(settings.PositionLocked);
        Assert.Equal(1.2, settings.TextScale);
    }

    [Fact]
    public void Choosing_a_font_in_the_manager_saves_it() => OnUiThread(() =>
    {
        var store = new DataStore(_directory);
        var manager = new ManagerWindow(store, focusAddQuote: false);
        var fonts = (IReadOnlyList<QuoteFont>)manager.FontBox.ItemsSource;

        Assert.Equal(QuoteFonts.DefaultName, ((QuoteFont)manager.FontBox.SelectedItem).Name);
        Assert.Contains(fonts, f => f.Name == "Great Vibes");

        manager.FontBox.SelectedItem = fonts.First(f => f.Name == "Great Vibes");
        Assert.Equal("Great Vibes", store.LoadSettings().Font);
    });

    [Fact]
    public void Ticking_lowercase_in_the_manager_saves_it() => OnUiThread(() =>
    {
        var store = new DataStore(_directory);
        var manager = new ManagerWindow(store, focusAddQuote: false);
        Assert.False(manager.LowercaseBox.IsChecked);

        manager.LowercaseBox.IsChecked = true;
        manager.LowercaseBox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.True(store.LoadSettings().Lowercase);

        var reopened = new ManagerWindow(store, focusAddQuote: false);
        Assert.True(reopened.LowercaseBox.IsChecked);
    });

    [Fact]
    public void Show_authors_and_lock_position_save_from_the_manager() => OnUiThread(() =>
    {
        var store = new DataStore(_directory);
        var manager = new ManagerWindow(store, focusAddQuote: false);
        Assert.True(manager.ShowAuthorsBox.IsChecked);
        Assert.False(manager.LockPositionBox.IsChecked);

        manager.ShowAuthorsBox.IsChecked = false;
        manager.ShowAuthorsBox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        manager.LockPositionBox.IsChecked = true;
        manager.LockPositionBox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        var saved = store.LoadSettings();
        Assert.False(saved.ShowAuthors);
        Assert.True(saved.PositionLocked);

        var reopened = new ManagerWindow(store, focusAddQuote: false);
        Assert.False(reopened.ShowAuthorsBox.IsChecked);
        Assert.True(reopened.LockPositionBox.IsChecked);
    });

    [Fact]
    public void Manager_picks_up_a_lock_set_from_the_widget() => OnUiThread(() =>
    {
        var store = new DataStore(_directory);
        var manager = new ManagerWindow(store, focusAddQuote: false);
        Assert.False(manager.LockPositionBox.IsChecked);

        store.UpdateSettings(s => s.PositionLocked = true); // What the widget's padlock button does.
        manager.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });

        Assert.True(manager.LockPositionBox.IsChecked);
    });

    [Fact]
    public void Other_manager_options_dont_undo_a_lock_set_from_the_widget() => OnUiThread(() =>
    {
        var store = new DataStore(_directory);
        var manager = new ManagerWindow(store, focusAddQuote: false);

        store.UpdateSettings(s => s.PositionLocked = true);
        manager.LowercaseBox.IsChecked = true;
        manager.LowercaseBox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        Assert.True(store.LoadSettings().Lowercase);
        Assert.True(store.LoadSettings().PositionLocked);
    });

    [Fact]
    public void Manager_keeps_a_chosen_font_when_reopened() => OnUiThread(() =>
    {
        var store = new DataStore(_directory);
        store.UpdateSettings(s => s.Font = "Parisienne");
        var manager = new ManagerWindow(store, focusAddQuote: false);
        Assert.Equal("Parisienne", ((QuoteFont)manager.FontBox.SelectedItem).Name);
    });
}
