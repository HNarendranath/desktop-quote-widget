namespace WindowsQuotes.Settings;

public enum WidgetLook
{
    BareText,
    Frosted,
    Tinted,
}

public enum WidgetControls
{
    HoverButtons,
    RightClickMenu,
    TrayOnly,
}

/// <summary>Position in physical pixels, relative to the monitor's work area.</summary>
public sealed record WidgetPosition(int X, int Y);

public sealed class AppSettings
{
    public static readonly int[] IntervalPresets = [15, 30, 60, 240, 1440];

    public WidgetLook Look { get; set; } = WidgetLook.BareText;
    public WidgetControls Controls { get; set; } = WidgetControls.HoverButtons;
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>How often your own quotes show, from 0 to 1, when other quotes are available.</summary>
    public double OwnShare { get; set; } = 0.5;

    public double TextScale { get; set; } = 1.0;

    /// <summary>Device name of the chosen monitor. Null means the primary monitor.</summary>
    public string? Monitor { get; set; }

    public Dictionary<string, WidgetPosition> Positions { get; set; } = [];

    /// <summary>Sources turned off. Sources are on unless listed here, so new ones default to on.</summary>
    public HashSet<string> DisabledSources { get; set; } = [];
}
