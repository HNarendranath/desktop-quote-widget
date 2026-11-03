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

/// <summary>How quote text is drawn.</summary>
public sealed record QuoteStyle(double Scale, string Font, bool Lowercase);

/// <summary>Where the widget's centre sits, in physical pixels, relative to the monitor's work area.</summary>
public sealed record WidgetCenter(int X, int Y);

public sealed class AppSettings
{
    public static readonly int[] IntervalPresets = [15, 30, 60, 240, 1440];

    public WidgetLook Look { get; set; } = WidgetLook.BareText;
    public WidgetControls Controls { get; set; } = WidgetControls.HoverButtons;
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>How often your added and starred quotes show, from 0 to 1, when other quotes are available.</summary>
    public double OwnShare { get; set; } = 0.5;

    public double TextScale { get; set; } = 1.0;

    /// <summary>The font's name in the widget's list. Unknown names use the default font.</summary>
    public string Font { get; set; } = "Sitka";

    /// <summary>Shows quotes in lowercase. Only the display changes, never the saved quote.</summary>
    public bool Lowercase { get; set; }

    /// <summary>Device name of the chosen monitor. Null means the primary monitor.</summary>
    public string? Monitor { get; set; }

    public Dictionary<string, WidgetCenter> Centers { get; set; } = [];

    /// <summary>Sources turned off. Sources are on unless listed here, so new ones default to on.</summary>
    public HashSet<string> DisabledSources { get; set; } = [];
}
