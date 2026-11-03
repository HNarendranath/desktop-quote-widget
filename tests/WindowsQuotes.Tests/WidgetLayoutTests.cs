using System.IO;
using System.IO.Packaging;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WindowsQuotes.Quotes;
using WindowsQuotes.Settings;
using WindowsQuotes.Widget;

namespace WindowsQuotes.Tests;

/// <summary>
/// Lays out and draws the real widget into a bitmap. The window is never shown, so these run
/// without taking focus or the mouse. Set WQ_RENDER_DIR to also save pictures of every font.
/// </summary>
public class WidgetLayoutTests
{
    private const string ShortQuote = "Begin.";
    private const string MediumQuote = "The obstacle in the path becomes the path. Never forget, within every difficulty lies opportunity.";
    private static readonly string LongQuote = string.Concat(Enumerable.Repeat("You have power over your mind, not outside events. Realise this and you will find strength. ", 3)).Trim();

    private static void OnUiThread(Action test)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = PackUriHelper.UriSchemePack; // Registers pack:// URIs, which WPF apps get automatically.
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

    private static WidgetWindow Laid(string text, string author, string font, WidgetLook look = WidgetLook.Tinted, bool lowercase = false, WidgetControls controls = WidgetControls.HoverButtons, double scale = 1)
    {
        var window = new WidgetWindow(look, controls, new QuoteStyle(scale, font, lowercase));
        window.SetQuote(new Quote(text, author, QuoteOrigin.Mine), animate: false);
        window.Root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        window.Root.Arrange(new Rect(window.Root.DesiredSize));
        window.Root.UpdateLayout();
        return window;
    }

    private static RenderTargetBitmap Draw(FrameworkElement element)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
    }

    /// <summary>Empty space from the bubble's edges to the text, by looking at the drawn pixels.</summary>
    private static (int Left, int Top, int Right, int Bottom) Gaps(BitmapSource bitmap)
    {
        var (width, height) = (bitmap.PixelWidth, bitmap.PixelHeight);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        int left = width, right = -1, top = height, bottom = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (pixels[((y * width) + x) * 4] > 100) // Light text on the dark card.
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }
        Assert.True(right >= 0, "nothing was drawn");
        return (left, top, width - 1 - right, height - 1 - bottom);
    }

    public static TheoryData<string> AvailableFonts()
    {
        var data = new TheoryData<string>();
        OnUiThread(() =>
        {
            foreach (var font in QuoteFonts.Available())
            {
                data.Add(font.Name);
            }
        });
        return data;
    }

    [Theory]
    [MemberData(nameof(AvailableFonts))]
    public void Text_is_centred_both_ways_in_every_font(string font) => OnUiThread(() =>
    {
        foreach (var (text, author) in new[] { (ShortQuote, string.Empty), (ShortQuote, "Seneca"), (MediumQuote, "Marcus Aurelius"), (LongQuote, "Epictetus") })
        {
            var gaps = Gaps(Draw(Laid(text, author, font).Card));
            Assert.True(Math.Abs(gaps.Top - gaps.Bottom) <= 3, $"{font}, '{text[..6]}', author '{author}': {gaps}");
            Assert.True(Math.Abs(gaps.Left - gaps.Right) <= 8, $"{font}, '{text[..6]}', author '{author}': {gaps}");
        }
    });

    [Theory]
    [MemberData(nameof(AvailableFonts))]
    public void Nothing_is_cut_off_or_touching_the_edge(string font) => OnUiThread(() =>
    {
        foreach (var text in new[] { ShortQuote, MediumQuote, LongQuote })
        {
            var gaps = Gaps(Draw(Laid(text, "Marcus Aurelius", font).Card));
            Assert.True(gaps.Left >= 12 && gaps.Right >= 12, $"{font}, '{text[..6]}': {gaps}");
            Assert.True(gaps.Top >= 30 && gaps.Bottom >= 30, $"{font}, '{text[..6]}': {gaps}");
        }
    });

    [Theory]
    [MemberData(nameof(AvailableFonts))]
    public void The_bubble_fits_the_quote(string font) => OnUiThread(() =>
    {
        var small = Laid(ShortQuote, string.Empty, font).Card;
        var medium = Laid(MediumQuote, "Marcus Aurelius", font).Card;
        var large = Laid(LongQuote, "Epictetus", font).Card;

        Assert.True(small.ActualWidth < medium.ActualWidth, $"{font}: {small.ActualWidth} vs {medium.ActualWidth}");
        Assert.True(small.ActualHeight < medium.ActualHeight);
        Assert.True(medium.ActualHeight < large.ActualHeight);
        Assert.True(large.ActualWidth <= 520, $"{font}: {large.ActualWidth}");
    });

    [Theory]
    [MemberData(nameof(AvailableFonts))]
    public void Longest_quote_fits_on_a_small_laptop_even_at_the_largest_text_size(string font) => OnUiThread(() =>
    {
        var longest = string.Concat(Enumerable.Repeat("word ", QuoteText.MaxOwnLength / 5)).Trim();
        var card = Laid(longest, "Someone", font, scale: 1.6).Card;
        Assert.True(card.ActualHeight <= 680, $"{font}: {card.ActualHeight}");
    });

    [Fact]
    public void Short_quote_still_has_room_for_all_the_buttons() => OnUiThread(() =>
    {
        var window = Laid("Hi", string.Empty, QuoteFonts.DefaultName);
        Assert.True(window.Card.ActualWidth >= window.Buttons.ActualWidth + 16, $"{window.Card.ActualWidth} vs {window.Buttons.ActualWidth}");
    });

    [Fact]
    public void Buttons_never_overlap_the_text() => OnUiThread(() =>
    {
        foreach (var font in QuoteFonts.Available())
        {
            var window = Laid(MediumQuote, "Marcus Aurelius", font.Name);
            var gaps = Gaps(Draw(window.Card));
            Assert.True(gaps.Bottom >= window.Buttons.ActualHeight + window.Buttons.Margin.Bottom, $"{font.Name}: {gaps}");
        }
    });

    [Fact]
    public void Without_buttons_the_bubble_is_tighter() => OnUiThread(() =>
    {
        var hover = Laid(MediumQuote, string.Empty, QuoteFonts.DefaultName).Card.ActualHeight;
        var tray = Laid(MediumQuote, string.Empty, QuoteFonts.DefaultName, controls: WidgetControls.TrayOnly).Card.ActualHeight;
        Assert.True(tray < hover);
    });

    [Fact]
    public void Lowercase_changes_the_display_only() => OnUiThread(() =>
    {
        var quote = new Quote("Know Thyself, O Stranger.", "SOCRATES", QuoteOrigin.Mine);
        var window = new WidgetWindow(WidgetLook.Tinted, WidgetControls.HoverButtons, new QuoteStyle(1, QuoteFonts.DefaultName, true));
        window.SetQuote(quote, animate: false);

        Assert.Equal("know thyself, o stranger.", window.QuoteLine.Text);
        Assert.Equal("— socrates", window.AuthorLine.Text);
        Assert.Equal("Know Thyself, O Stranger.", quote.Text);

        window.SetStyle(new QuoteStyle(1, QuoteFonts.DefaultName, false));
        Assert.Equal("Know Thyself, O Stranger.", window.QuoteLine.Text);
    });

    [Fact]
    public void Authors_can_be_hidden() => OnUiThread(() =>
    {
        var quote = new Quote(MediumQuote, "Marcus Aurelius", QuoteOrigin.Mine);
        var window = new WidgetWindow(WidgetLook.Tinted, WidgetControls.HoverButtons, new QuoteStyle(1, QuoteFonts.DefaultName, false));
        window.SetQuote(quote, animate: false);
        Assert.Equal("— Marcus Aurelius", window.AuthorLine.Text);
        Assert.Equal(Visibility.Visible, window.AuthorLine.Visibility);

        window.SetStyle(new QuoteStyle(1, QuoteFonts.DefaultName, false, ShowAuthor: false));
        Assert.Equal(string.Empty, window.AuthorLine.Text);
        Assert.Equal(Visibility.Collapsed, window.AuthorLine.Visibility);
        Assert.Equal("Marcus Aurelius", quote.Author);

        window.SetStyle(new QuoteStyle(1, QuoteFonts.DefaultName, false));
        Assert.Equal("— Marcus Aurelius", window.AuthorLine.Text);
    });

    [Fact]
    public void Without_an_author_the_bubble_is_shorter_and_still_centred() => OnUiThread(() =>
    {
        WidgetWindow Build(bool show)
        {
            var window = new WidgetWindow(WidgetLook.Tinted, WidgetControls.HoverButtons, new QuoteStyle(1, "Dancing Script", false, show));
            window.SetQuote(new Quote(MediumQuote, "Marcus Aurelius", QuoteOrigin.Mine), animate: false);
            window.Root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            window.Root.Arrange(new Rect(window.Root.DesiredSize));
            window.Root.UpdateLayout();
            return window;
        }

        var hidden = Build(false).Card;
        Assert.True(hidden.ActualHeight < Build(true).Card.ActualHeight);
        var gaps = Gaps(Draw(hidden));
        Assert.True(Math.Abs(gaps.Top - gaps.Bottom) <= 3, gaps.ToString());
    });

    private static void PressOnQuote(WidgetWindow window) =>
        window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });

    [Fact]
    public void Pressing_the_quote_starts_a_drag_only_when_unlocked() => OnUiThread(() =>
    {
        var free = Laid(MediumQuote, string.Empty, QuoteFonts.DefaultName);
        PressOnQuote(free);
        Assert.True(free.IsPressed);

        var locked = Laid(MediumQuote, string.Empty, QuoteFonts.DefaultName);
        locked.SetPositionLocked(true);
        PressOnQuote(locked);
        Assert.False(locked.IsPressed);

        locked.SetPositionLocked(false);
        PressOnQuote(locked);
        Assert.True(locked.IsPressed);
    });

    /// <summary>Alpha values of a button drawn on its own, enlarged so the icon's shape is clear.</summary>
    private static byte[] IconAlpha(System.Windows.Controls.Button button)
    {
        const int Size = 240;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new VisualBrush(button), null, new Rect(0, 0, Size, Size));
        }
        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[Size * Size * 4];
        bitmap.CopyPixels(pixels, Size * 4, 0);
        return pixels.Where((_, i) => i % 4 == 3).ToArray();
    }

    [Fact]
    public void Held_pin_fills_the_handle_and_keeps_the_point() => OnUiThread(() =>
    {
        var window = Laid(MediumQuote, string.Empty, QuoteFonts.DefaultName);

        window.SetHeld(false);
        window.Root.UpdateLayout();
        var outline = IconAlpha(window.HoldButton);

        window.SetHeld(true);
        window.Root.UpdateLayout();
        var held = IconAlpha(window.HoldButton);

        var outlineInk = outline.Count(a => a > 200);
        var heldInk = held.Count(a => a > 200);
        var kept = outline.Zip(held).Count(p => p.First > 200 && p.Second > 128);

        Assert.True(outlineInk > 500, $"the outline barely drew: {outlineInk} pixels");
        Assert.True(heldInk > outlineInk * 1.3, $"held {heldInk} vs outline {outlineInk} pixels");

        // This fails if the held icon loses the needle, because every outline pixel must survive.
        Assert.True(kept >= outlineInk * 0.99, $"only {kept} of {outlineInk} outline pixels survive");

        window.SetHeld(false);
        window.Root.UpdateLayout();
        Assert.Equal(outlineInk, IconAlpha(window.HoldButton).Count(a => a > 200));
    });

    [Fact]
    public void Lock_button_shows_the_state_and_asks_to_toggle() => OnUiThread(() =>
    {
        var window = Laid(MediumQuote, string.Empty, QuoteFonts.DefaultName);
        var clicks = 0;
        window.LockRequested += () => clicks++;

        window.SetPositionLocked(false);
        var open = (string)window.LockButton.Content;
        window.SetPositionLocked(true);
        var closed = (string)window.LockButton.Content;
        Assert.NotEqual(open, closed);

        var iconFont = new Typeface(window.LockButton.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        Assert.True(iconFont.TryGetGlyphTypeface(out var glyphs));
        Assert.True(glyphs.CharacterToGlyphMap.ContainsKey(open[0]));
        Assert.True(glyphs.CharacterToGlyphMap.ContainsKey(closed[0]));

        window.LockButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(1, clicks);
    });

    [Fact]
    public void Bundled_fonts_really_load_rather_than_falling_back() => OnUiThread(() =>
    {
        foreach (var font in QuoteFonts.All.Where(f => f.Bundled))
        {
            var typeface = new Typeface(font.Family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            Assert.True(typeface.TryGetGlyphTypeface(out var glyphs), font.Name);
            Assert.Contains(font.Name, glyphs.Win32FamilyNames.Values.First());
        }
    });

    [Fact]
    public void Unknown_font_uses_the_default() => OnUiThread(() =>
    {
        Assert.Equal(QuoteFonts.DefaultName, QuoteFonts.Find("Not A Font").Name);
        Assert.Equal(QuoteFonts.DefaultName, QuoteFonts.Find(null).Name);
    });

    [Fact]
    public void Saves_pictures_of_every_font_when_asked() => OnUiThread(() =>
    {
        var folder = Environment.GetEnvironmentVariable("WQ_RENDER_DIR");
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }
        Directory.CreateDirectory(folder);

        var fonts = QuoteFonts.Available();
        const int Columns = 3, CellWidth = 540, CellHeight = 300;
        var sheet = new DrawingVisual();
        using (var dc = sheet.RenderOpen())
        {
            var rows = (int)Math.Ceiling(fonts.Count / (double)Columns);
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(38, 52, 74), Color.FromRgb(72, 60, 84), 45), null, new Rect(0, 0, Columns * CellWidth, rows * CellHeight));
            for (var i = 0; i < fonts.Count; i++)
            {
                var card = Laid(MediumQuote, "Marcus Aurelius", fonts[i].Name).Card;
                var image = Draw(card);
                var cell = new Rect((i % Columns) * CellWidth, (i / Columns) * CellHeight, CellWidth, CellHeight);
                dc.DrawImage(image, new Rect(cell.X + ((CellWidth - image.Width) / 2), cell.Y + ((CellHeight - image.Height) / 2), image.Width, image.Height));
                dc.DrawText(new FormattedText(fonts[i].Name, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13, Brushes.White, 1), new Point(cell.X + 8, cell.Y + 6));
            }
        }
        var rendered = new RenderTargetBitmap(Columns * CellWidth, (int)Math.Ceiling(fonts.Count / (double)Columns) * CellHeight, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(sheet);
        Save(rendered, Path.Combine(folder, "fonts.png"));

        // One font across the three kinds of quote and the lowercase setting.
        var sizes = new DrawingVisual();
        using (var dc = sizes.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(45, 58, 78)), null, new Rect(0, 0, 1100, 760));
            var (x, y) = (20.0, 20.0);
            foreach (var (text, author, lower) in new[] { (ShortQuote, "Seneca", false), ("Hi", string.Empty, false), (MediumQuote, "Marcus Aurelius", true), (LongQuote, "Epictetus", false) })
            {
                var image = Draw(Laid(text, author, "Dancing Script", lowercase: lower).Card);
                dc.DrawImage(image, new Rect(x, y, image.Width, image.Height));
                x += image.Width + 20;
                if (x > 560)
                {
                    x = 20;
                    y += 300;
                }
            }
        }
        var sizesBitmap = new RenderTargetBitmap(1100, 760, 96, 96, PixelFormats.Pbgra32);
        sizesBitmap.Render(sizes);
        Save(sizesBitmap, Path.Combine(folder, "sizes.png"));

        // The hover buttons, off and on, enlarged.
        var row = new DrawingVisual();
        using (var dc = row.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 32, 40)), null, new Rect(0, 0, 860, 420));
            foreach (var (active, y) in new[] { (false, 20.0), (true, 220.0) })
            {
                var window = Laid(MediumQuote, string.Empty, QuoteFonts.DefaultName);
                window.SetHeld(active);
                window.SetPositionLocked(active);
                window.SetStarred(active);
                window.Buttons.Opacity = 1; // They only fade in on hover.
                window.Root.UpdateLayout();
                dc.DrawRectangle(new VisualBrush(window.Buttons), null, new Rect(20, y, window.Buttons.ActualWidth * 5, window.Buttons.ActualHeight * 5));
            }
        }
        var rowBitmap = new RenderTargetBitmap(860, 420, 96, 96, PixelFormats.Pbgra32);
        rowBitmap.Render(row);
        Save(rowBitmap, Path.Combine(folder, "buttons.png"));
    });

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
