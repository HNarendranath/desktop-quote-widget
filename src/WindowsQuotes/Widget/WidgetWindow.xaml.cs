using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WindowsQuotes.Quotes;
using WindowsQuotes.Settings;

namespace WindowsQuotes.Widget;

/// <summary>The visible quote. Knows how to draw itself and reports clicks to <see cref="WidgetHost"/>.</summary>
public partial class WidgetWindow : Window
{
    private const double BaseQuoteSize = 28;
    private const double BaseAuthorSize = 15;
    private const double MaxTextWidth = 456;

    /// <summary>Keeps the bubble shorter than a small laptop's screen at any text size, so it stays above the taskbar.</summary>
    private const double MaxTextHeight = 520;
    private const double MinFontSize = 10;
    private const double ShrinkStep = 0.92;

    private const double AuthorGap = 12;
    private const double SidePadding = 32;
    private const double PaddingWithButtons = 40;
    private const double PaddingWithoutButtons = 26;
    private const int AnimationFrameRate = 30;
    private const int DragThresholdPixels = 4;

    private const string LockedGlyph = "";
    private const string UnlockedGlyph = "";
    private const string StarGlyph = "";
    private const string FilledStarGlyph = "";

    private static readonly Duration FadeOut = TimeSpan.FromMilliseconds(170);
    private static readonly Duration FadeIn = TimeSpan.FromMilliseconds(230);
    private static readonly Duration ButtonFade = TimeSpan.FromMilliseconds(130);

    private readonly bool _frosted;
    private readonly bool _debugFloat;
    private WidgetControls _controls;
    private QuoteStyle _style;
    private bool _positionLocked;
    private DesktopPinner? _pinner;
    private Quote? _quote;
    private bool _pressed;
    private (int X, int Y) _pressCursor;
    private PixelRect _pressRect;

    /// <param name="debugFloat">Float above other windows instead of pinning, so a look can be checked without clearing the desktop.</param>
    public WidgetWindow(WidgetLook look, WidgetControls controls, QuoteStyle style, bool debugFloat = false)
    {
        _frosted = look == WidgetLook.Frosted;
        _debugFloat = debugFloat;
        _controls = controls;
        _style = style;

        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/WindowsQuotes;component/Widget/Themes/{look}.xaml"),
        });

        InitializeComponent();
        ApplyPadding();

        // Only a non-layered window can have the frosted backdrop, and this can't change
        // after the window is shown, which is why changing look recreates the window.
        AllowsTransparency = !_frosted;
        Topmost = debugFloat;
    }

    public event Action? NextRequested;
    public event Action? HoldMenuRequested;
    public event Action? CopyRequested;
    public event Action? StarRequested;
    public event Action? LockRequested;
    public event Action? ContextMenuRequested;
    public event Action? Moved;

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    /// <summary>True while the user is dragging, when the widget should go where the mouse goes.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>True between pressing on the quote and letting go, when moving the mouse drags the widget.</summary>
    internal bool IsPressed => _pressed;

    /// <summary>Chooses the position for a new size. See <see cref="DesktopPinner.PlacementForSize"/>.</summary>
    public Func<int, int, (int X, int Y)?>? PlacementForSize { get; set; }

    /// <summary>Attaches to the desktop again, after Explorer restarts or displays change.</summary>
    public void Repin() => _pinner?.Pin();

    public void SetQuote(Quote? quote, bool animate)
    {
        if (!animate || !IsVisible)
        {
            _quote = quote;
            ApplyText();
            return;
        }

        var fadeOut = new DoubleAnimation(0, FadeOut);
        Timeline.SetDesiredFrameRate(fadeOut, AnimationFrameRate);
        fadeOut.Completed += (_, _) =>
        {
            _quote = quote;
            ApplyText();
            var fadeIn = new DoubleAnimation(1, FadeIn);
            Timeline.SetDesiredFrameRate(fadeIn, AnimationFrameRate);
            fadeIn.Completed += (_, _) =>
            {
                // Release the animation clocks, so nothing stays subscribed to rendering.
                Card.BeginAnimation(OpacityProperty, null);
                Card.Opacity = 1;
            };
            Card.BeginAnimation(OpacityProperty, fadeIn);
        };
        Card.BeginAnimation(OpacityProperty, fadeOut);
    }

    public void SetHeld(bool held)
    {
        PinFill.Visibility = held ? Visibility.Visible : Visibility.Collapsed;
        HoldButton.ToolTip = held ? "Held. Click to change or resume" : "Hold this quote";
        AutomationProperties.SetName(HoldButton, held ? "Quote held" : "Hold this quote");
    }

    /// <summary>Null hides the star, for quotes you added yourself, which are already yours.</summary>
    public void SetStarred(bool? starred)
    {
        StarButton.Visibility = starred is null ? Visibility.Collapsed : Visibility.Visible;
        StarButton.Content = starred == true ? FilledStarGlyph : StarGlyph;
        StarButton.ToolTip = starred == true ? "Remove star" : "Star this quote";
    }

    public void SetStyle(QuoteStyle style)
    {
        _style = style;
        ApplyText();
    }

    public void SetControls(WidgetControls controls)
    {
        _controls = controls;
        ApplyPadding();
        ApplyInputMode();
    }

    public void SetPositionLocked(bool locked)
    {
        _positionLocked = locked;
        LockButton.Content = locked ? LockedGlyph : UnlockedGlyph;
        LockButton.ToolTip = locked ? "Position locked. Click to unlock" : "Lock position";
        AutomationProperties.SetName(LockButton, locked ? "Position locked" : "Lock position");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _pinner = new DesktopPinner(Handle, pinToDesktop: !_debugFloat) { PlacementForSize = PlacementForSize };
        if (_frosted)
        {
            Backdrop.ApplyFrosted(Handle);
        }
        ApplyInputMode();
    }

    protected override void OnClosed(EventArgs e)
    {
        _pinner?.Dispose();
        base.OnClosed(e);
    }

    private void ApplyText()
    {
        var text = ForDisplay(_quote?.Text);
        var author = _style.ShowAuthor ? ForDisplay(_quote?.Author) : string.Empty;
        if (author.Length > 0)
        {
            author = $"— {author}";
        }

        var font = QuoteFonts.Find(_style.Font);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var size = BaseQuoteSize * _style.Scale * font.SizeScale * QuoteText.ScaleForLength(text.Length);
        var quoteFit = QuoteLayout.Fit(text, font.Family, size, size * font.LineSpacing, MaxTextWidth, pixelsPerDip);
        while (quoteFit.Height > MaxTextHeight && size > MinFontSize)
        {
            size *= ShrinkStep;
            quoteFit = QuoteLayout.Fit(text, font.Family, size, size * font.LineSpacing, MaxTextWidth, pixelsPerDip);
        }
        QuoteLine.FontFamily = font.Family;
        QuoteLine.FontSize = size;
        QuoteLine.LineHeight = size * font.LineSpacing;
        QuoteLine.Width = quoteFit.Width;
        QuoteLine.Margin = new Thickness(0, -quoteFit.InkAbove, 0, -quoteFit.InkBelow);
        QuoteLine.Text = text;

        var authorSize = BaseAuthorSize * _style.Scale;
        var authorFit = QuoteLayout.Fit(author, AuthorLine.FontFamily, authorSize, authorSize * 1.3, MaxTextWidth, pixelsPerDip);
        AuthorLine.FontSize = authorSize;
        AuthorLine.MaxWidth = MaxTextWidth;
        AuthorLine.Margin = new Thickness(0, AuthorGap - authorFit.InkAbove, 0, -authorFit.InkBelow);
        AuthorLine.Text = author;
        AuthorLine.Visibility = author.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private string ForDisplay(string? value)
    {
        var text = value ?? string.Empty;
        return _style.Lowercase ? text.ToLower(CultureInfo.CurrentCulture) : text;
    }

    private void ApplyPadding()
    {
        var vertical = _controls == WidgetControls.HoverButtons ? PaddingWithButtons : PaddingWithoutButtons;
        Card.Padding = new Thickness(SidePadding, vertical, SidePadding, vertical);
    }

    private void ApplyInputMode()
    {
        _pinner?.SetClickThrough(_controls == WidgetControls.TrayOnly);
        if (_controls != WidgetControls.HoverButtons)
        {
            ShowButtons(false);
        }
    }

    private void ShowButtons(bool show)
    {
        Buttons.IsHitTestVisible = show;
        var fade = new DoubleAnimation(show ? 1 : 0, ButtonFade);
        Timeline.SetDesiredFrameRate(fade, AnimationFrameRate);
        fade.Completed += (_, _) =>
        {
            Buttons.BeginAnimation(OpacityProperty, null);
            Buttons.Opacity = show ? 1 : 0;
        };
        Buttons.BeginAnimation(OpacityProperty, fade);
    }

    private void OnMouseEnterWidget(object sender, MouseEventArgs e)
    {
        if (_controls == WidgetControls.HoverButtons)
        {
            ShowButtons(true);
        }
    }

    private void OnMouseLeaveWidget(object sender, MouseEventArgs e)
    {
        if (Buttons.IsHitTestVisible)
        {
            ShowButtons(false);
        }
    }

    private void OnLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_positionLocked)
        {
            return;
        }

        // Buttons handle their own clicks, so this is a press on the quote itself.
        _pressed = true;
        _pressCursor = WindowGeometry.Cursor();
        _pressRect = WindowGeometry.RectOf(Handle);
        CaptureMouse();
    }

    private void OnDragMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pressed)
        {
            return;
        }
        var cursor = WindowGeometry.Cursor();
        var dx = cursor.X - _pressCursor.X;
        var dy = cursor.Y - _pressCursor.Y;
        if (!IsDragging && Math.Abs(dx) + Math.Abs(dy) < DragThresholdPixels)
        {
            return;
        }
        if (!IsDragging)
        {
            IsDragging = true;
            Cursor = Cursors.SizeAll;
        }
        WindowGeometry.MoveTo(Handle, _pressRect.Left + dx, _pressRect.Top + dy);
    }

    private void OnLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressed)
        {
            ReleaseMouseCapture(); // Raises LostMouseCapture, which finishes the drag.
        }
    }

    private void OnDragCaptureLost(object sender, MouseEventArgs e)
    {
        _pressed = false;
        if (IsDragging)
        {
            IsDragging = false;
            Cursor = null;
            Moved?.Invoke();
        }
    }

    private void OnRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_controls == WidgetControls.RightClickMenu)
        {
            ContextMenuRequested?.Invoke();
            e.Handled = true;
        }
    }

    private void OnNextClick(object sender, RoutedEventArgs e) => NextRequested?.Invoke();
    private void OnHoldClick(object sender, RoutedEventArgs e) => HoldMenuRequested?.Invoke();
    private void OnCopyClick(object sender, RoutedEventArgs e) => CopyRequested?.Invoke();
    private void OnStarClick(object sender, RoutedEventArgs e) => StarRequested?.Invoke();
    private void OnLockClick(object sender, RoutedEventArgs e) => LockRequested?.Invoke();
}
