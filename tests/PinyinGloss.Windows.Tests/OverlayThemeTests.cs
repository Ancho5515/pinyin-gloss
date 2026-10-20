using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using PinyinGloss.Core;
using PinyinGloss.Desktop;
using SystemColors = System.Windows.SystemColors;

namespace PinyinGloss.Windows.Tests;

public class OverlayThemeTests
{
    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, true)]
    [InlineData(true, true, null, true)]
    public void Theme_respects_system_mode_manual_override_and_read_failure(bool follow, bool manualDark, bool? systemDark, bool expectedDark)
    {
        var theme = OverlayTheme.Create(new() { FollowSystemTheme = follow, Dark = manualDark }, systemDark, false);
        Assert.Equal(expectedDark, theme.Dark);
    }
    [Fact]
    public void Existing_settings_default_to_following_system_without_losing_manual_value()
    {
        var display = JsonSerializer.Deserialize<DisplaySettings>("{\"dark\":true}", SettingsJson.Options)!;
        Assert.True(display.FollowSystemTheme); Assert.True(display.Dark);
    }
    [Theory]
    [InlineData(true, "#000000")]
    [InlineData(false, "#FFFFFF")]
    public void Unreadable_custom_column_color_falls_back_to_readable_foreground(bool dark, string unreadable)
    {
        var theme = OverlayTheme.Create(new() { Dark = dark, FollowSystemTheme = false }, null, false);
        Assert.Equal(theme.Foreground, theme.ColumnColor(new() { ColorDark = unreadable, ColorLight = unreadable }));
    }
    [Fact]
    public void High_contrast_uses_system_background_and_text_for_all_columns()
    {
        var theme = OverlayTheme.Create(new(), true, true);
        Assert.Equal(SystemColors.WindowColor, theme.Background);
        Assert.Equal(SystemColors.WindowTextColor, theme.Foreground);
        Assert.Equal(theme.Foreground, theme.ColumnColor(new()));
    }
}
