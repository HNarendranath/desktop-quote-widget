using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace WindowsQuotes.Widget;

/// <summary>
/// How much room a piece of text takes.
/// <paramref name="Width"/> is the narrowest width that doesn't add a line. It evens out the
/// lines and lets the bubble shrink to fit the text.
/// <paramref name="InkAbove"/> and <paramref name="InkBelow"/> are the empty space between the
/// text's box and its visible shapes. Fonts differ a lot here, so removing it is what makes
/// the text look centred in every font.
/// <paramref name="Height"/> is the height of the text's box.
/// </summary>
public readonly record struct TextFit(double Width, double InkAbove, double InkBelow, double Height);

public static class QuoteLayout
{
    private const int SearchSteps = 8;

    public static TextFit Fit(string text, FontFamily family, double size, double lineHeight, double maxWidth, double pixelsPerDip)
    {
        if (text.Length == 0)
        {
            return default;
        }

        var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        FormattedText Format(double width) => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, Brushes.White, pixelsPerDip)
        {
            MaxTextWidth = width,
            LineHeight = lineHeight,
            Trimming = TextTrimming.None,
        };

        var widest = Format(maxWidth);
        var height = widest.Height;
        var fits = Math.Min(maxWidth, widest.Width);

        // Lines can't be evened out to less than half the longest one without adding a line,
        // unless the text is several lines long, where stopping there still looks balanced.
        var tooNarrow = fits / 2;
        for (var i = 0; i < SearchSteps; i++)
        {
            var middle = (tooNarrow + fits) / 2;
            if (Format(middle).Height <= height + 0.5)
            {
                fits = middle;
            }
            else
            {
                tooNarrow = middle;
            }
        }

        var chosen = Format(fits);
        var ink = chosen.BuildGeometry(new Point()).Bounds;
        var width = Math.Min(maxWidth, Math.Ceiling(fits) + 1);
        return ink.IsEmpty
            ? new TextFit(width, 0, 0, chosen.Height)
            : new TextFit(width, ink.Top, chosen.Height - ink.Bottom, chosen.Height);
    }
}
