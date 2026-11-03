using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WindowsQuotes.Widget;

/// <summary>
/// A hidden window that outlives the visible widget, which is recreated when the look
/// changes. It owns the tray icon's messages, menus, and system notifications. Being
/// event-driven, it never wakes the app unless something actually happens.
/// </summary>
internal sealed class HostWindow : IDisposable
{
    public const string Title = "WindowsQuotes.Host";
    public const int TrayCallbackMessage = 0x8000 + 1; // WM_APP + 1

    private const int WM_DISPLAYCHANGE = 0x007E;
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WM_POWERBROADCAST = 0x0218;
    private const int WM_WTSSESSION_CHANGE = 0x02B1;
    private const int SPI_SETWORKAREA = 0x002F;
    private const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    private const int PBT_POWERSETTINGCHANGE = 0x8013;
    private const int WTS_SESSION_LOCK = 7;
    private const int WTS_SESSION_UNLOCK = 8;
    private const uint DEVICE_NOTIFY_WINDOW_HANDLE = 0;
    private const uint NOTIFY_FOR_THIS_SESSION = 0;

    private static readonly Guid GUID_CONSOLE_DISPLAY_STATE = new("6FE69556-704A-47A0-8F24-C28D936FDA47");
    private static readonly uint WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");
    private static readonly uint WM_RELOAD = RegisterWindowMessage("WindowsQuotes.Reload");

    private readonly HwndSource _source;
    private readonly IntPtr _powerNotification;
    private bool _locked;
    private bool _displayOff;

    public HostWindow()
    {
        _source = new HwndSource(new HwndSourceParameters(Title) { WindowStyle = 0, Width = 0, Height = 0 });
        _source.AddHook(WndProc);
        WTSRegisterSessionNotification(Handle, NOTIFY_FOR_THIS_SESSION);
        var guid = GUID_CONSOLE_DISPLAY_STATE;
        _powerNotification = RegisterPowerSettingNotification(Handle, ref guid, DEVICE_NOTIFY_WINDOW_HANDLE);
    }

    public IntPtr Handle => _source.Handle;

    /// <summary>True while the session is locked or the display is off, when nobody can see the widget.</summary>
    public bool UserAway => _locked || _displayOff;

    /// <summary>Raised when the tray icon reports a mouse event. The argument is the mouse message.</summary>
    public event Action<int>? TrayEvent;

    /// <summary>Raised when Explorer has restarted, so the tray icon must be added again.</summary>
    public event Action? TaskbarCreated;

    /// <summary>Raised when the manager has saved changes.</summary>
    public event Action? ReloadRequested;

    /// <summary>Raised when monitors or the work area change.</summary>
    public event Action? DisplayChanged;

    /// <summary>Raised when the user comes back, or the PC wakes, so overdue work can run.</summary>
    public event Action? UserReturned;

    /// <summary>Tells a running widget to re-read its files. Does nothing if none is running.</summary>
    public static void NotifyReload()
    {
        var host = FindWindow(null, Title);
        if (host != IntPtr.Zero)
        {
            PostMessage(host, WM_RELOAD, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case TrayCallbackMessage:
                TrayEvent?.Invoke((int)(lParam.ToInt64() & 0xFFFF));
                handled = true;
                break;

            case WM_DISPLAYCHANGE:
                DisplayChanged?.Invoke();
                break;

            case WM_SETTINGCHANGE when wParam.ToInt64() == SPI_SETWORKAREA:
                DisplayChanged?.Invoke();
                break;

            case WM_WTSSESSION_CHANGE:
                OnSessionChange(wParam.ToInt32());
                break;

            case WM_POWERBROADCAST:
                OnPowerBroadcast(wParam.ToInt32(), lParam);
                break;

            default:
                if (msg == WM_TASKBARCREATED)
                {
                    TaskbarCreated?.Invoke();
                }
                else if (msg == WM_RELOAD)
                {
                    ReloadRequested?.Invoke();
                    handled = true;
                }
                break;
        }
        return IntPtr.Zero;
    }

    private void OnSessionChange(int change)
    {
        if (change == WTS_SESSION_LOCK)
        {
            _locked = true;
        }
        else if (change == WTS_SESSION_UNLOCK)
        {
            _locked = false;
            RaiseIfBack();
        }
    }

    private void OnPowerBroadcast(int kind, IntPtr data)
    {
        if (kind == PBT_APMRESUMEAUTOMATIC)
        {
            RaiseIfBack();
        }
        else if (kind == PBT_POWERSETTINGCHANGE)
        {
            var setting = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(data);
            if (setting.PowerSetting == GUID_CONSOLE_DISPLAY_STATE)
            {
                _displayOff = setting.Data == 0;
                RaiseIfBack();
            }
        }
    }

    private void RaiseIfBack()
    {
        if (!UserAway)
        {
            UserReturned?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_powerNotification != IntPtr.Zero)
        {
            UnregisterPowerSettingNotification(_powerNotification);
        }
        WTSUnRegisterSessionNotification(Handle);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSRegisterSessionNotification(IntPtr hwnd, uint flags);

    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid powerSetting, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterPowerSettingNotification(IntPtr handle);
}
