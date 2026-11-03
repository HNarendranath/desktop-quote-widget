using System.Runtime.InteropServices;

namespace WindowsQuotes.Widget;

/// <summary>A display, in physical pixels.</summary>
public sealed record MonitorInfo(string Device, PixelRect Bounds, PixelRect WorkArea, bool IsPrimary);

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

public static class Monitors
{
    private const uint MONITORINFOF_PRIMARY = 1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    public static IReadOnlyList<MonitorInfo> All()
    {
        var monitors = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (handle, _, _, _) =>
        {
            if (Describe(handle) is { } info)
            {
                monitors.Add(info);
            }
            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    /// <summary>The named monitor, or the primary one if it's not named or no longer connected.</summary>
    public static MonitorInfo Resolve(string? device)
    {
        var all = All();
        return all.FirstOrDefault(m => m.Device == device)
            ?? all.FirstOrDefault(m => m.IsPrimary)
            ?? all[0];
    }

    public static MonitorInfo? ForWindow(IntPtr hwnd) => Describe(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST));

    private static MonitorInfo? Describe(IntPtr handle)
    {
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfo(handle, ref info))
        {
            return null;
        }
        return new MonitorInfo(
            info.szDevice,
            new PixelRect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom),
            new PixelRect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom),
            (info.dwFlags & MONITORINFOF_PRIMARY) != 0);
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
}
