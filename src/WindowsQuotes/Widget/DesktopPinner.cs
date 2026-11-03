using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WindowsQuotes.Widget;

/// <summary>
/// Keeps a window on the desktop layer: above the wallpaper, below normal windows,
/// out of Alt+Tab and the taskbar, and never activated. This relies on undocumented
/// shell behaviour, so all of it stays in this one class.
/// </summary>
internal sealed class DesktopPinner : IDisposable
{
    private const int GWL_EXSTYLE = -20;
    private const int GWLP_HWNDPARENT = -8;

    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_APPWINDOW = 0x00040000;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private const long WS_EX_TRANSPARENT = 0x00000020;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int WM_DISPLAYCHANGE = 0x007E;
    private const int MA_NOACTIVATE = 3;

    private static readonly IntPtr HWND_BOTTOM = new(1);
    private static readonly uint WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");

    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly bool _pinToDesktop;

    /// <param name="pinToDesktop">False keeps every behaviour except the z-order, for checking a look while floating on top.</param>
    public DesktopPinner(IntPtr hwnd, bool pinToDesktop = true)
    {
        _hwnd = hwnd;
        _pinToDesktop = pinToDesktop;
        _source = HwndSource.FromHwnd(hwnd)
            ?? throw new InvalidOperationException("Window has no HwndSource yet.");
        _source.AddHook(WndProc);

        var exStyle = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        exStyle = (exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(exStyle));

        Pin();
    }

    /// <summary>
    /// Chooses the position whenever the window is resized. Deciding this while the resize
    /// is still happening means the new size and position apply together, so a longer quote
    /// can never appear behind the taskbar even briefly. Returning null leaves it alone.
    /// </summary>
    public Func<int, int, (int X, int Y)?>? PlacementForSize { get; set; }

    /// <summary>Makes all mouse input pass through the window, or restores it.</summary>
    public void SetClickThrough(bool enabled)
    {
        var exStyle = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        exStyle = enabled ? exStyle | WS_EX_TRANSPARENT : exStyle & ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
    }

    /// <summary>Attaches to the current desktop window and moves to the bottom of the stack.</summary>
    public void Pin()
    {
        if (!_pinToDesktop)
        {
            return;
        }

        var progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            // An owned window stays above its owner, and windows owned by the desktop
            // are left alone by Show Desktop (Win+D).
            SetWindowLongPtr(_hwnd, GWLP_HWNDPARENT, progman);
        }
        SetWindowPos(_hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_MOUSEACTIVATE:
                handled = true;
                return new IntPtr(MA_NOACTIVATE);

            case WM_WINDOWPOSCHANGING:
                var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);

                // Whatever asks to raise this window, send it back to the bottom.
                if (_pinToDesktop && (pos.flags & SWP_NOZORDER) == 0)
                {
                    pos.hwndInsertAfter = HWND_BOTTOM;
                }
                if ((pos.flags & SWP_NOSIZE) == 0 && PlacementForSize?.Invoke(pos.cx, pos.cy) is { } placement)
                {
                    pos.x = placement.X;
                    pos.y = placement.Y;
                    pos.flags &= ~SWP_NOMOVE;
                }
                Marshal.StructureToPtr(pos, lParam, false);
                break;

            case WM_DISPLAYCHANGE:
                Pin();
                break;

            default:
                if (msg == WM_TASKBARCREATED)
                {
                    // Explorer restarted, so the old Progman window is gone.
                    Pin();
                }
                break;
        }
        return IntPtr.Zero;
    }

    public void Dispose() => _source.RemoveHook(WndProc);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
