using System.Windows.Media;

namespace WindowsQuotes.Widget;

/// <summary>
/// A font the quote can use. Scripts look small at the same point size, so each has its own
/// size and line spacing to keep every font about as readable as the default.
/// </summary>
public sealed class QuoteFont(string name, string source, double sizeScale = 1, double lineSpacing = 1.3, bool bundled = false)
{
    private const string Fallback = "Sitka Display, Georgia";
    private static readonly Uri BundledFontsBase = new("pack://application:,,,/WindowsQuotes;component/");

    private FontFamily? _family;

    public string Name { get; } = name;
    public double SizeScale { get; } = sizeScale;
    public double LineSpacing { get; } = lineSpacing;
    public bool Bundled { get; } = bundled;

    /// <summary>Created when first needed, so fonts that aren't chosen are never loaded.</summary>
    public FontFamily Family => _family ??= Bundled
        ? new FontFamily(BundledFontsBase, $"./Assets/Fonts/{source}, {Fallback}")
        : new FontFamily($"{source}, {Fallback}");
}

public static class QuoteFonts
{
    public const string DefaultName = "Sitka";

    public static readonly IReadOnlyList<QuoteFont> All =
    [
        new(DefaultName, "Sitka Display"),
        new("Georgia", "Georgia", sizeScale: 0.92),
        new("Palatino", "Palatino Linotype", sizeScale: 0.95),
        new("Segoe UI", "Segoe UI", sizeScale: 0.9),

        new("Dancing Script", "DancingScript.ttf#Dancing Script", sizeScale: 1.15, lineSpacing: 1.25, bundled: true),
        new("Great Vibes", "GreatVibes-Regular.ttf#Great Vibes", sizeScale: 1.3, lineSpacing: 1.2, bundled: true),
        new("Parisienne", "Parisienne-Regular.ttf#Parisienne", sizeScale: 1.15, lineSpacing: 1.3, bundled: true),
        new("Sacramento", "Sacramento-Regular.ttf#Sacramento", sizeScale: 1.35, lineSpacing: 1.15, bundled: true),

        new("Segoe Script", "Segoe Script", sizeScale: 0.8, lineSpacing: 1.45),
        new("Segoe Print", "Segoe Print", sizeScale: 0.85, lineSpacing: 1.4),
        new("Ink Free", "Ink Free", sizeScale: 1.05, lineSpacing: 1.25),
        new("Gabriola", "Gabriola", sizeScale: 1.25, lineSpacing: 1.05),
        new("Edwardian Script", "Edwardian Script ITC", sizeScale: 1.4, lineSpacing: 1.1),
        new("Monotype Corsiva", "Monotype Corsiva", sizeScale: 1.1, lineSpacing: 1.25),
        new("Brush Script", "Brush Script MT", sizeScale: 1.2, lineSpacing: 1.2),
        new("Lucida Handwriting", "Lucida Handwriting", sizeScale: 0.8, lineSpacing: 1.4),
    ];

    /// <summary>The named font, or the default if it's unknown, for example from an older settings file.</summary>
    public static QuoteFont Find(string? name) =>
        All.FirstOrDefault(f => f.Name == name) ?? All[0];

    /// <summary>
    /// The fonts this PC can show. Some of the list only comes with Office. This asks Windows
    /// for every installed font, so it's for the manager rather than the always-running widget.
    /// </summary>
    public static IReadOnlyList<QuoteFont> Available()
    {
        var installed = Fonts.SystemFontFamilies.Select(f => f.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return All.Where(f => f.Bundled || installed.Contains(SystemFamilyName(f))).ToList();
    }

    private static string SystemFamilyName(QuoteFont font) => font.Family.Source.Split(',')[0].Trim();
}
