using System.Runtime.InteropServices;
using WindowsQuotes.Settings;

namespace WindowsQuotes.Widget;

/// <summary>
/// Works out where the widget goes. Its centre is the fixed point, so a longer quote grows
/// evenly up and down, and it's only ever pushed away from the centre to stay in the work
/// area, which excludes the taskbar.
/// </summary>
public static class WidgetPlacement
{
    private const double DefaultMarginDip = 48;

    /// <summary>A typical size, so the default centre doesn't depend on the current quote.</summary>
    private const double NominalWidthDip = 520;
    private const double NominalHeightDip = 190;

    /// <summary>The top-left corner for a window of this size.</summary>
    public static (int X, int Y) TopLeft(PixelRect work, WidgetCenter? saved, int width, int height, double dpiScale)
    {
        var center = saved ?? DefaultCenter(work, dpiScale);
        return Clamp(work, work.Left + center.X - (width / 2), work.Top + center.Y - (height / 2), width, height);
    }

    /// <summary>Pulls a dropped window fully on screen and returns the centre to remember.</summary>
    public static WidgetCenter CenterOnDrop(PixelRect work, int left, int top, int width, int height)
    {
        var (x, y) = Clamp(work, left, top, width, height);
        return new WidgetCenter(x + (width / 2) - work.Left, y + (height / 2) - work.Top);
    }

    private static WidgetCenter DefaultCenter(PixelRect work, double dpiScale)
    {
        var margin = (int)(DefaultMarginDip * dpiScale);
        var width = (int)(NominalWidthDip * dpiScale);
        var height = (int)(NominalHeightDip * dpiScale);
        return new WidgetCenter(work.Width - margin - (width / 2), work.Height - margin - (height / 2));
    }

    private static (int X, int Y) Clamp(PixelRect work, int x, int y, int width, int height)
    {
        // Math.Max comes last, so if it's ever taller than the screen the top stays visible.
        return (
            Math.Max(work.Left, Math.Min(x, work.Right - width)),
            Math.Max(work.Top, Math.Min(y, work.Bottom - height)));
    }
}

/// <summary>Physical-pixel window operations shared by the window and its host.</summary>
internal static class WindowGeometry
{
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    public static PixelRect RectOf(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out var rect);
        return new PixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    public static void MoveTo(IntPtr hwnd, int x, int y) =>
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

    public static (int X, int Y) Cursor()
    {
        GetCursorPos(out var point);
        return (point.X, point.Y);
    }

    public static double DpiScale(IntPtr hwnd) => GetDpiForWindow(hwnd) / 96.0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
