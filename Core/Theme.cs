using System.Windows;
using System.Windows.Media;

namespace Droideck.Core;

public static class Theme
{
    static Brush B(byte r, byte g, byte b)
    {
        var x = new SolidColorBrush(Color.FromRgb(r, g, b));
        x.Freeze();
        return x;
    }

    public static readonly Brush Accent = B(53, 116, 240);
    public static readonly Brush Ok = B(95, 184, 101);
    public static readonly Brush Warn = B(242, 197, 92);
    public static readonly Brush Danger = B(247, 84, 100);
    public static readonly Brush Muted = B(134, 138, 145);
    public static readonly Brush Fg = B(223, 225, 229);
    public static readonly Brush Info = B(104, 151, 187);
    public static readonly Brush Pink = B(255, 107, 104);
    public static readonly Brush Violet = B(152, 118, 170);
    public static readonly Brush Folder = B(220, 182, 122);

    public static readonly Brush LogVerbose = B(187, 187, 187);
    public static readonly Brush LogDebug = B(104, 151, 187);
    public static readonly Brush LogInfo = B(106, 135, 89);
    public static readonly Brush LogWarn = B(187, 181, 41);
    public static readonly Brush LogError = B(255, 107, 104);

    public static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public static Style Sty(string key) => (Style)Application.Current.FindResource(key);
}
