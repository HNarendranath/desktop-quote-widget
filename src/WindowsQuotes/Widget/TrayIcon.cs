using System.Runtime.InteropServices;

namespace WindowsQuotes.Widget;

/// <summary>The notification area icon, using Shell_NotifyIcon directly so WinForms never loads.</summary>
internal sealed class TrayIcon : IDisposable
{
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONUP = 0x0205;

    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;
    private const int MaxTooltip = 127;

    private readonly IntPtr _host;
    private readonly IntPtr _icon;
    private string _tooltip = "Quotes";

    public TrayIcon(IntPtr host)
    {
        _host = host;
        var path = Environment.ProcessPath ?? string.Empty;
        ExtractIconEx(path, 0, IntPtr.Zero, out _icon, 1);
        if (_icon == IntPtr.Zero)
        {
            const int IDI_APPLICATION = 32512;
            _icon = LoadIcon(IntPtr.Zero, new IntPtr(IDI_APPLICATION));
        }
        Add();
    }

    /// <summary>Adds the icon. Also called after Explorer restarts, which forgets all icons.</summary>
    public void Add() => Shell_NotifyIcon(NIM_ADD, Data());

    public void SetTooltip(string text)
    {
        _tooltip = text.Length > MaxTooltip ? text[..MaxTooltip] : text;
        Shell_NotifyIcon(NIM_MODIFY, Data());
    }

    public void Dispose()
    {
        Shell_NotifyIcon(NIM_DELETE, Data());
        DestroyIcon(_icon);
    }

    private NOTIFYICONDATA Data() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _host,
        uID = 1,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage = HostWindow.TrayCallbackMessage,
        hIcon = _icon,
        szTip = _tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, in NOTIFYICONDATA data);

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr large, out IntPtr small, uint count);

    [DllImport("user32.dll", EntryPoint = "LoadIconW")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
