using System.Windows;
using System.Windows.Interop;

namespace WindowsQuotes.Widget;

public partial class WidgetWindow : Window
{
    private readonly bool _frosted;
    private DesktopPinner? _pinner;

    private readonly bool _debugFloat;

    /// <param name="frosted">Use the frosted glass look.</param>
    /// <param name="debugFloat">Float above other windows instead of pinning, so the look can be checked without clearing the desktop.</param>
    public WidgetWindow(bool frosted, bool debugFloat = false)
    {
        _frosted = frosted;
        _debugFloat = debugFloat;
        InitializeComponent();
        AllowsTransparency = !frosted;
        Topmost = debugFloat;

        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 48;
        Top = area.Bottom - 260;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!_debugFloat)
        {
            _pinner = new DesktopPinner(hwnd);
        }
        if (_frosted)
        {
            Backdrop.ApplyFrosted(hwnd);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _pinner?.Dispose();
        base.OnClosed(e);
    }
}
