using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using WindowsQuotes.Quotes;
using WindowsQuotes.Settings;

namespace WindowsQuotes.Widget;

/// <summary>The visible quote. Knows how to draw itself and reports clicks to <see cref="WidgetHost"/>.</summary>
public partial class WidgetWindow : Window
{
    private const double BaseQuoteSize = 28;
    private const double BaseAuthorSize = 15;
    private const int AnimationFrameRate = 30;

    private const string PinGlyph = "";
    private const string PinnedGlyph = "";
    private const string StarGlyph = "";
    private const string FilledStarGlyph = "";

    private static readonly Duration FadeOut = TimeSpan.FromMilliseconds(170);
    private static readonly Duration FadeIn = TimeSpan.FromMilliseconds(230);
    private static readonly Duration ButtonFade = TimeSpan.FromMilliseconds(130);

    private readonly bool _frosted;
    private readonly bool _debugFloat;
    private WidgetControls _controls;
    private double _textScale;
    private bool _moveMode;
    private DesktopPinner? _pinner;
    private Quote? _quote;

    /// <param name="debugFloat">Float above other windows instead of pinning, so a look can be checked without clearing the desktop.</param>
    public WidgetWindow(WidgetLook look, WidgetControls controls, double textScale, bool debugFloat = false)
    {
        _frosted = look == WidgetLook.Frosted;
        _debugFloat = debugFloat;
        _controls = controls;
        _textScale = textScale;

        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Widget/Themes/{look}.xaml"),
        });

        InitializeComponent();

        // Only a non-layered window can have the frosted backdrop, and this can't change
        // after the window is shown, which is why changing look recreates the window.
        AllowsTransparency = !_frosted;
        Topmost = debugFloat;
    }

    public event Action? NextRequested;
    public event Action? HoldMenuRequested;
    public event Action? CopyRequested;
    public event Action? FavouriteRequested;
    public event Action? ContextMenuRequested;
    public event Action? Moved;

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

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
        HoldButton.Content = held ? PinnedGlyph : PinGlyph;
        HoldButton.ToolTip = held ? "Held. Click to change or resume" : "Hold this quote";
    }

    public void SetSaved(bool saved)
    {
        FavouriteButton.Content = saved ? FilledStarGlyph : StarGlyph;
        FavouriteButton.ToolTip = saved ? "In My Quotes" : "Save to My Quotes";
    }

    public void SetTextScale(double scale)
    {
        _textScale = scale;
        ApplyText();
    }

    public void SetControls(WidgetControls controls)
    {
        _controls = controls;
        ApplyInputMode();
    }

    public void SetMoveMode(bool enabled)
    {
        _moveMode = enabled;
        MoveOutline.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        Cursor = enabled ? Cursors.SizeAll : null;
        ApplyInputMode();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (!_debugFloat)
        {
            _pinner = new DesktopPinner(Handle);
        }
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
        var text = _quote?.Text ?? string.Empty;
        var author = _quote?.Author ?? string.Empty;
        QuoteLine.FontSize = BaseQuoteSize * _textScale * QuoteText.ScaleForLength(text.Length);
        AuthorLine.FontSize = BaseAuthorSize * _textScale;
        QuoteLine.Text = text;
        AuthorLine.Text = author.Length == 0 ? string.Empty : $"— {author}";
    }

    private void ApplyInputMode()
    {
        _pinner?.SetClickThrough(_controls == WidgetControls.TrayOnly && !_moveMode);
        if (_moveMode || _controls != WidgetControls.HoverButtons)
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
        if (_controls == WidgetControls.HoverButtons && !_moveMode)
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
        if (_moveMode && e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            Moved?.Invoke();
        }
    }

    private void OnRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_controls == WidgetControls.RightClickMenu && !_moveMode)
        {
            ContextMenuRequested?.Invoke();
            e.Handled = true;
        }
    }

    private void OnNextClick(object sender, RoutedEventArgs e) => NextRequested?.Invoke();
    private void OnHoldClick(object sender, RoutedEventArgs e) => HoldMenuRequested?.Invoke();
    private void OnCopyClick(object sender, RoutedEventArgs e) => CopyRequested?.Invoke();
    private void OnFavouriteClick(object sender, RoutedEventArgs e) => FavouriteRequested?.Invoke();
}
