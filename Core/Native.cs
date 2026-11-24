using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Droideck.Core;

public static class Native
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void DarkTitleBar(Window w)
    {
        try
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            int on = 1;
            DwmSetWindowAttribute(h, 19, ref on, 4);
            DwmSetWindowAttribute(h, 20, ref on, 4);
            int caption = 0x00302D2B;
            DwmSetWindowAttribute(h, 35, ref caption, 4);
            int text = 0x00E5E1DF;
            DwmSetWindowAttribute(h, 36, ref text, 4);
        }
        catch
        {
        }
    }
}
