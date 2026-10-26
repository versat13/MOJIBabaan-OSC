using System.Windows.Media;

namespace MOJIBabaan.Osc.Gui;

static class Palette
{
    static SolidColorBrush B(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public static readonly Brush Gold = B("#F2C94C");
    public static readonly Brush GoldSoft = B("#33F2C94C");
    public static readonly Brush GoldLine = B("#80F2C94C");
    public static readonly Brush Red = B("#FF6B6B");
    public static readonly Brush RedSoft = B("#55D13438");
    public static readonly Brush RedLine = B("#FFE5484D");
    public static readonly Brush Green = B("#5BD98B");
    public static readonly Brush Amber = B("#E8A93B");
    public static readonly Brush Gray = B("#80808A");
    public static readonly Brush Faint = B("#14FFFFFF");
    public static readonly Brush FaintLine = B("#22FFFFFF");
    public static readonly Brush Text = B("#E8E8EC");
    public static readonly Brush TextDim = B("#A0A0AA");
}