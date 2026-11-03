using System.Windows;
using WindowsQuotes.Widget;

namespace WindowsQuotes;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        new WidgetWindow(
            frosted: e.Args.Contains("--frosted"),
            debugFloat: e.Args.Contains("--debug-float")).Show();
    }
}
