using WindowsQuotes.Settings;
using WindowsQuotes.Widget;

namespace WindowsQuotes.Tests;

public class PlacementTests
{
    // A 1920x1080 screen with a 48px taskbar along the bottom.
    private static readonly PixelRect Work = new(0, 0, 1920, 1032);

    [Fact]
    public void Centre_stays_fixed_as_height_changes()
    {
        var center = new WidgetCenter(800, 500);
        var (_, shortTop) = WidgetPlacement.TopLeft(Work, center, 520, 100, 1);
        var (_, tallTop) = WidgetPlacement.TopLeft(Work, center, 520, 300, 1);
        Assert.Equal(500, shortTop + 50);
        Assert.Equal(500, tallTop + 150);
    }

    [Fact]
    public void Centre_stays_fixed_horizontally()
    {
        var (x, _) = WidgetPlacement.TopLeft(Work, new WidgetCenter(800, 500), 520, 100, 1);
        Assert.Equal(800, x + 260);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(300)]
    [InlineData(700)]
    public void Bottom_never_goes_below_the_work_area(int height)
    {
        var (_, y) = WidgetPlacement.TopLeft(Work, new WidgetCenter(800, 1000), 520, height, 1);
        Assert.True(y + height <= Work.Bottom);
    }

    [Fact]
    public void Default_position_keeps_every_quote_size_above_the_taskbar()
    {
        foreach (var height in new[] { 100, 190, 300, 500, 800 })
        {
            var (x, y) = WidgetPlacement.TopLeft(Work, null, 520, height, 1);
            Assert.True(y + height <= Work.Bottom, $"height {height}");
            Assert.True(x + 520 <= Work.Right);
        }
    }

    [Fact]
    public void Top_stays_visible_if_taller_than_the_screen()
    {
        var (_, y) = WidgetPlacement.TopLeft(Work, new WidgetCenter(800, 500), 520, 2000, 1);
        Assert.Equal(Work.Top, y);
    }

    [Fact]
    public void Works_on_a_monitor_that_isnt_at_the_origin()
    {
        var second = new PixelRect(1920, -200, 3840, 880);
        var (x, y) = WidgetPlacement.TopLeft(second, new WidgetCenter(100, 5000), 520, 200, 1);
        Assert.Equal(1920, x);
        Assert.Equal(680, y);
    }

    [Fact]
    public void Dropping_partly_off_screen_remembers_a_centre_that_is_on_screen()
    {
        var center = WidgetPlacement.CenterOnDrop(Work, 1800, 1000, 520, 200);
        var (x, y) = WidgetPlacement.TopLeft(Work, center, 520, 200, 1);
        Assert.Equal(1400, x);
        Assert.Equal(832, y);
    }

    [Fact]
    public void Dropping_on_screen_round_trips_exactly()
    {
        var center = WidgetPlacement.CenterOnDrop(Work, 300, 400, 520, 200);
        Assert.Equal((300, 400), WidgetPlacement.TopLeft(Work, center, 520, 200, 1));
    }
}
