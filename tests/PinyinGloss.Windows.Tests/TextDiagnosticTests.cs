using PinyinGloss.Core;
using PinyinGloss.Desktop;

namespace PinyinGloss.Windows.Tests;

public class TextDiagnosticTests
{
    [Fact]
    public void A_late_target_status_after_focus_loss_cannot_reopen_the_overlay()
    {
        foreach (var status in new[] { TextReadStatus.Reading, TextReadStatus.Success, TextReadStatus.InvalidTextScope })
            Assert.False(Accepts(null, null, new(status, 10, "edit", "focus", 1)));
    }
    [Fact]
    public void A_late_reading_status_after_capture_failure_cannot_replace_the_error()
    {
        var focus = new InputFocus("focus", 10, "edit", false);
        Assert.False(Accepts(focus, null, new(TextReadStatus.Reading, 10, "edit", "focus", 1)));
        Assert.True(Accepts(focus, null, new(TextReadStatus.InvalidTextScope, 10, "edit", "focus", 1)));
    }
    [Fact]
    public void Reading_feedback_requires_the_current_confirmed_snapshot()
    {
        var focus = new InputFocus("focus", 10, "edit", true);
        var snapshot = new TextSnapshot("focus", 2, 10, "edit", TextMode.Sentence, "苹果。");
        Assert.True(Accepts(focus, snapshot, new(TextReadStatus.Reading, 10, "edit", "focus", 2)));
        Assert.False(Accepts(focus, snapshot, new(TextReadStatus.Reading, 10, "edit", "focus", 1)));
        Assert.False(Accepts(focus, snapshot, new(TextReadStatus.Reading, 10, "other", "other", 2)));
        Assert.True(Accepts(null, null, new(TextReadStatus.NoFocusedTarget, 10)));
        Assert.True(Accepts(null, null, new(TextReadStatus.AccessDenied, 10)));
    }
    private static bool Accepts(InputFocus? focus, TextSnapshot? snapshot, TextReadDiagnostic diagnostic)
    {
        var method = typeof(DesktopRuntime).GetMethod("AcceptsTextDiagnostic", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (bool)method.Invoke(null, [focus, snapshot, diagnostic, 10L])!;
    }
}
