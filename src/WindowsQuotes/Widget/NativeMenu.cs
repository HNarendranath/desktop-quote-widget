using System.Runtime.InteropServices;

namespace WindowsQuotes.Widget;

/// <summary>One line in a <see cref="NativeMenu"/>.</summary>
internal sealed record MenuEntry(string Label, Action? Action = null, bool Checked = false, IReadOnlyList<MenuEntry>? Children = null)
{
    public static readonly MenuEntry Separator = new(string.Empty);
}

/// <summary>
/// A plain Win32 popup menu. Used for the tray, the right-click menu and the hold button,
/// because it costs nothing until it's opened and needs no WPF styling.
/// </summary>
internal static class NativeMenu
{
    private const uint MF_STRING = 0x0000;
    private const uint MF_CHECKED = 0x0008;
    private const uint MF_POPUP = 0x0010;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint WM_NULL = 0x0000;

    private static bool s_darkModeSet;

    /// <summary>Shows the menu at the cursor, blocks until it closes, then runs the chosen action.</summary>
    public static void Show(IntPtr owner, IReadOnlyList<MenuEntry> entries)
    {
        FollowSystemDarkMode();

        var actions = new List<Action?>();
        var menu = Build(entries, actions);
        try
        {
            GetCursorPos(out var point);

            // The menu only closes on an outside click if its owner is in the foreground.
            // Give focus back afterwards so the widget never leaves the user's app unfocused.
            var previous = GetForegroundWindow();
            SetForegroundWindow(owner);
            var command = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, point.X, point.Y, owner, IntPtr.Zero);
            PostMessage(owner, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            if (previous != IntPtr.Zero)
            {
                SetForegroundWindow(previous);
            }

            if (command > 0)
            {
                actions[command - 1]?.Invoke();
            }
        }
        finally
        {
            DestroyMenu(menu); // Also destroys submenus.
        }
    }

    private static IntPtr Build(IReadOnlyList<MenuEntry> entries, List<Action?> actions)
    {
        var menu = CreatePopupMenu();
        foreach (var entry in entries)
        {
            if (entry == MenuEntry.Separator)
            {
                AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            }
            else if (entry.Children is { } children)
            {
                AppendMenu(menu, MF_POPUP, (nuint)Build(children, actions), entry.Label);
            }
            else
            {
                actions.Add(entry.Action);
                AppendMenu(menu, MF_STRING | (entry.Checked ? MF_CHECKED : 0), (nuint)actions.Count, entry.Label);
            }
        }
        return menu;
    }

    private static void FollowSystemDarkMode()
    {
        if (s_darkModeSet)
        {
            return;
        }
        s_darkModeSet = true;
        try
        {
            // Undocumented, but the only way to get dark Win32 menus. A failure just means light menus.
            const int AllowDark = 1;
            SetPreferredAppMode(AllowDark);
            FlushMenuThemes();
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, nuint idNewItem, string? newItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
