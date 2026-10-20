using System.Windows;
using System.Windows.Media;
using PinyinGloss.Core;
using Microsoft.Win32;

namespace PinyinGloss.Desktop;

internal sealed record OverlayTheme(bool Dark, bool HighContrast, Color Background, Color Foreground, Color Border)
{
    internal const double CornerRadius = 8;
    internal static void ApplyTypography(Window window) { window.FontFamily = new("Microsoft YaHei UI"); window.FontSize = 14; }
    internal static OverlayTheme Create(DisplaySettings settings, bool? systemDark, bool highContrast)
    {
        var dark = settings.FollowSystemTheme ? systemDark ?? settings.Dark : settings.Dark;
        if (highContrast) return new(dark, true, SystemColors.WindowColor, SystemColors.WindowTextColor, SystemColors.WindowTextColor);
        return dark ? new(true, false, Color.FromRgb(44, 44, 44), Color.FromRgb(242, 242, 242), Color.FromRgb(85, 85, 85)) :
            new(false, false, Color.FromRgb(249, 249, 249), Color.FromRgb(27, 27, 27), Color.FromRgb(210, 210, 210));
    }
    internal Color ColumnColor(ColumnSettings column)
    {
        if (HighContrast) return Foreground;
        var color = (Color)ColorConverter.ConvertFromString(Dark ? column.ColorDark : column.ColorLight);
        var light = Luminance(color); var background = Luminance(Background);
        return (Math.Max(light, background) + 0.05) / (Math.Min(light, background) + 0.05) >= 4.5 ? color : Foreground;
    }
    private static double Luminance(Color color)
    {
        static double Linear(byte value) { var c = value / 255d; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }
    internal static bool? ReadSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int mode && mode is 0 or 1 ? mode == 0 : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return null; }
    }
}
