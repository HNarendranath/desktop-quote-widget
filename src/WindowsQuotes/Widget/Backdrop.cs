using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace WindowsQuotes.Widget;

/// <summary>
/// Applies the frosted glass effect to a non-layered window. The documented DWM system
/// backdrop turns flat grey on inactive windows, and this window is never active, so
/// this uses the undocumented accent API, which blurs regardless of activation.
/// </summary>
internal static class Backdrop
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    /// <summary>Tint over the blur, as 0xAABBGGRR.</summary>
    private const uint FrostedTint = 0x50201A18;

    public static void ApplyFrosted(IntPtr hwnd)
    {
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
        {
            target.BackgroundColor = Colors.Transparent;
        }

        var corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        var accent = new ACCENT_POLICY
        {
            AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
            GradientColor = FrostedTint,
        };
        var handle = GCHandle.Alloc(accent, GCHandleType.Pinned);
        try
        {
            var data = new WINDOWCOMPOSITIONATTRIBDATA
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = handle.AddrOfPinnedObject(),
                SizeOfData = Marshal.SizeOf<ACCENT_POLICY>(),
            };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            handle.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ACCENT_POLICY
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWCOMPOSITIONATTRIBDATA
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
