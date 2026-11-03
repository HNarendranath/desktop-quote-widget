using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using WindowsQuotes.Manager;
using WindowsQuotes.Quotes;
using WindowsQuotes.Widget;

namespace WindowsQuotes;

public partial class App : Application
{
    private const string WidgetMutexName = @"Local\WindowsQuotes.Widget";
    private const string ManagerMutexName = @"Local\WindowsQuotes.Manager";

    private static string s_logDirectory = Path.GetTempPath();

    private Mutex? _instanceMutex;
    private WidgetHost? _host;

    /// <summary>Appends to error.log in the data folder. Logging must never itself crash the app.</summary>
    public static void LogError(Exception exception)
    {
        try
        {
            File.AppendAllText(Path.Combine(s_logDirectory, "error.log"), $"{DateTimeOffset.Now:O} {exception}\n\n");
        }
        catch (IOException)
        {
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var dataDir = ArgumentAfter(e.Args, "--data-dir");
        var store = dataDir is null ? DataStore.CreateDefault() : new DataStore(dataDir);
        s_logDirectory = dataDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsQuotes");

        if (e.Args.Contains("--manager"))
        {
            RunManager(store, dataDir, e.Args.Contains("--add"));
        }
        else
        {
            RunWidget(store, dataDir, e.Args.Contains("--debug-float"));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void RunWidget(DataStore store, string? dataDir, bool debugFloat)
    {
        if (!TryBecomeOnlyInstance(WidgetMutexName, dataDir))
        {
            // Already running, so starting it again just opens the manager.
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add("--manager");
            if (dataDir is not null)
            {
                start.ArgumentList.Add("--data-dir");
                start.ArgumentList.Add(dataDir);
            }
            Process.Start(start)?.Dispose();
            Shutdown();
            return;
        }

        _host = new WidgetHost(store, debugFloat, dataDir);
        _host.Start();
    }

    private void RunManager(DataStore store, string? dataDir, bool focusAddQuote)
    {
        if (!TryBecomeOnlyInstance(ManagerMutexName, dataDir))
        {
            BringExistingManagerForward();
            Shutdown();
            return;
        }

        // The Fluent theme is only worth loading here. The widget draws its own look.
#pragma warning disable WPF0001 // ThemeMode is marked as evaluation-only by the SDK.
        ThemeMode = ThemeMode.System;
#pragma warning restore WPF0001

        var window = new ManagerWindow(store, focusAddQuote);
        window.Closed += (_, _) => Shutdown();
        window.Show();
    }

    /// <summary>Uses a separate mutex per data folder, so test runs don't collide with the real app.</summary>
    private bool TryBecomeOnlyInstance(string name, string? dataDir)
    {
        var suffix = dataDir is null ? string.Empty : "." + Path.GetFileName(dataDir.TrimEnd('\\', '/'));
        _instanceMutex = new Mutex(true, name + suffix, out var isFirst);
        return isFirst;
    }

    private static void BringExistingManagerForward()
    {
        const int SW_RESTORE = 9;
        var existing = FindWindow(null, "Windows Quotes");
        if (existing != IntPtr.Zero)
        {
            if (IsIconic(existing))
            {
                ShowWindow(existing, SW_RESTORE);
            }
            SetForegroundWindow(existing);
        }
    }

    private static string? ArgumentAfter(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A cosmetic widget should keep running rather than vanish from the desktop.
        LogError(e.Exception);
        e.Handled = true;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
