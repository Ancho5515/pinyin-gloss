using System.Windows.Threading;
using PinyinGloss.Core;
using PinyinGloss.Desktop;

namespace PinyinGloss.Windows.Tests;

public class TextOverlayTests
{
    public static IEnumerable<object[]> NonSuccessReadStatuses() =>
        from status in Enum.GetValues<TextReadStatus>() where status != TextReadStatus.Success
        from previouslyTranslated in new[] { false, true }
        select new object[] { status, previouslyTranslated };

    [Theory]
    [MemberData(nameof(NonSuccessReadStatuses))]
    public Task Non_successful_reads_collapse_and_reject_late_translations(TextReadStatus status, bool previouslyTranslated) => OnSta(() =>
    {
        var window = new TextOverlayWindow();
        try
        {
            var snapshot = new TextSnapshot("focus", 1, 10, "paragraph", TextMode.Selection, "我喜欢苹果。");
            if (previouslyTranslated)
                window.Present(snapshot, new("text", snapshot.Text, "en", ResultStatus.Ready, "I like apples."));
            for (var i = 0; i < 3; i++)
            {
                window.PresentReadStatus(new(status, 10, "paragraph", "focus", 2));
                Assert.Equal(28, window.Width); Assert.Equal(28, window.Height);
                Assert.Equal("", Texts(window.Content));
            }
            window.UpdateResult(new(snapshot, 1, "v1", new("text", snapshot.Text, "en", ResultStatus.Ready, "Late translation.")));
            Assert.Equal(28, window.Width); Assert.Equal("", Texts(window.Content));
            window.Present(snapshot with { Version = 3 }, new("text", snapshot.Text, "en", ResultStatus.Ready, "I like apples."));
            Assert.Equal(420, window.Width); Assert.Contains("I like apples.", Texts(window.Content));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task A_slow_read_restores_the_complete_translation_when_the_snapshot_is_unchanged() => OnSta(() =>
    {
        var window = new TextOverlayWindow();
        try
        {
            var snapshot = new TextSnapshot("focus", 1, 10, "edit", TextMode.Sentence, "我喜欢苹果。");
            window.Present(snapshot, new("focus/1", snapshot.Text, "en", ResultStatus.Ready, "I like apples."));
            var diagnostic = new TextReadDiagnostic(TextReadStatus.Reading, 10, "edit", "focus", 1);
            window.PresentReadStatus(diagnostic);
            Assert.Equal(28, window.Width); Assert.Equal("", Texts(window.Content));
            window.PresentReadStatus(diagnostic with { Status = TextReadStatus.Success, SessionId = "other" });
            Assert.Equal(28, window.Width);
            window.PresentReadStatus(diagnostic with { Status = TextReadStatus.Success });
            Assert.Contains("I like apples.", Texts(window.Content));
            Assert.DoesNotContain("正在读取", Texts(window.Content));
        }
        finally { window.Close(); }
    });
    [Theory]
    [InlineData("invalid_text_scope", "范围无效")]
    [InlineData("no_suitable_provider", "没有可用的文本读取接口")]
    [InlineData("access_denied", "访问被拒绝")]
    [InlineData("no_caret", "无法确认活动光标")]
    public Task Read_failure_snapshots_collapse_and_clear_old_content(string status, string expected) => OnSta(() =>
    {
        var window = new TextOverlayWindow();
        try
        {
            var snapshot = new TextSnapshot("focus", 1, 10, "edit", TextMode.Sentence, "旧原文。");
            window.Present(snapshot, new("focus/1", snapshot.Text, "en", ResultStatus.Ready, "Old translation."));
            window.Present(snapshot with { Version = 2, Text = "", Status = status }, null);
            var content = Texts(window.Content);
            Assert.DoesNotContain("旧原文", content);
            Assert.DoesNotContain("Old translation", content);
            Assert.Equal(28, window.Width); Assert.Equal("", content);
            Assert.Contains(expected, window.StatusText);
            window.Collapse();
            Assert.Contains(expected, window.LastNotice);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(0, 0, 1280, 688, 1600, 1200, 0, 0, 1280, 688)]
    [InlineData(-1920, 458, 1920, 1040, 2400, 1400, -1920, 458, 1920, 1040)]
    [InlineData(0, 0, 1280, 688, 420, 180, 844, 492, 420, 180)]
    public void Restored_window_fits_inside_the_available_work_area(double x, double y, double workWidth, double workHeight,
        double width, double height, double left, double top, double expectedWidth, double expectedHeight)
    {
        var work = new System.Windows.Rect(x, y, workWidth, workHeight);
        var bounds = TextOverlayWindow.FitToWorkArea(work, width, height, null, null);
        Assert.Equal(new System.Windows.Rect(left, top, expectedWidth, expectedHeight), bounds);
    }

    [Theory]
    [InlineData(ResultStatus.NotFound, null)]
    [InlineData(ResultStatus.Timeout, null)]
    [InlineData(ResultStatus.Cancelled, null)]
    [InlineData(ResultStatus.InvalidOutput, "output_truncated")]
    [InlineData(ResultStatus.Unavailable, null)]
    [InlineData(ResultStatus.Unavailable, "model_loading")]
    [InlineData(ResultStatus.Skipped, null)]
    [InlineData(ResultStatus.Pending, null)]
    public Task Unsuccessful_results_stay_collapsed_and_a_successful_retry_expands(ResultStatus status, string? error) => OnSta(() =>
    {
        var window = new TextOverlayWindow();
        try
        {
            var snapshot = new TextSnapshot("focus", 1, 10, "edit", TextMode.Sentence, "我喜欢苹果。");
            window.Present(snapshot, null);
            Assert.Equal(28, window.Width); Assert.Equal("", Texts(window.Content));
            window.UpdateResult(new(snapshot, 1, "v1", new("focus/1", snapshot.Text, "en", status, ErrorCode: error)));
            Assert.Equal(28, window.Width); Assert.Equal("", Texts(window.Content));
            window.UpdateResult(new(snapshot, 1, "v1", new("focus/1", snapshot.Text, "en", ResultStatus.Ready, "I like apples.")));
            Assert.Equal(420, window.Width); Assert.Contains("I like apples.", Texts(window.Content));
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task Text_window_can_expand_again_after_focus_loss() => OnSta(() =>
    {
        var window = new TextOverlayWindow();
        try
        {
            var settings = AppSettings.CreateDefault(); settings.TextTranslation.SentenceEnabled = true;
            window.Configure(settings);
            for (var version = 1; version <= 3; version++)
            {
                var snapshot = new TextSnapshot("focus", version, 10, "edit", TextMode.Sentence, "我喜欢苹果。");
                window.Present(snapshot, new("text", snapshot.Text, "en", ResultStatus.Ready, "I like apples."));
                Assert.Equal(420, window.Width); Assert.Contains(snapshot.Text, Texts(window.Content));
                window.Collapse(); Assert.Equal(28, window.Width); Assert.Equal("", Texts(window.Content));
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Composition_collapses_instead_of_displaying_the_previous_translation() => OnSta(() =>
    {
        var window = new TextOverlayWindow();
        try
        {
            var settings = AppSettings.CreateDefault(); settings.TextTranslation.SentenceEnabled = true;
            window.Configure(settings);
            var snapshot = new TextSnapshot("focus", 1, 10, "edit", TextMode.Sentence, "我喜欢苹果。");
            window.Present(snapshot, new("focus/1", snapshot.Text, "en", ResultStatus.Ready, "I like apples."));
            window.Present(snapshot with { Version = 2, Text = "", Status = "composing" }, null);
            var content = Texts(window.Content);
            Assert.Equal(28, window.Width); Assert.Equal("", content);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Text_and_candidate_windows_use_identical_typography() => OnSta(() =>
    {
        var candidate = new OverlayWindow(); var text = new TextOverlayWindow();
        try
        {
            Assert.Equal(candidate.FontFamily.Source, text.FontFamily.Source);
            Assert.Equal(candidate.FontSize, text.FontSize); Assert.Equal(14, text.FontSize);
        }
        finally { candidate.Close(); text.Close(); }
    });
    [Fact]
    public Task Text_window_starts_collapsed_and_rejects_other_session_results() => OnSta(() =>
    {
        var type = typeof(DesktopRuntime).Assembly.GetType("PinyinGloss.Desktop.TextOverlayWindow"); Assert.NotNull(type);
        var window = (System.Windows.Window)Activator.CreateInstance(type)!;
        try
        {
            var settings = AppSettings.CreateDefault(); settings.TextTranslation.SentenceEnabled = true;
            type.GetMethod("Configure")!.Invoke(window, [settings]);
            Assert.Equal(28, window.Width); Assert.Equal(28, window.Height);
            Assert.False(window.ShowActivated); Assert.False(window.Focusable);
            var snapshot = new TextSnapshot("focus", 1, 10, "edit", TextMode.Sentence, "我喜欢苹果。");
            type.GetMethod("Present")!.Invoke(window, [snapshot, null]);
            Assert.Equal(28, window.Width); Assert.Equal(28, window.Height);
            var stale = new TextTranslation(snapshot with { SessionId = "other" }, 1, "v1", new("text", snapshot.Text, "en", ResultStatus.Ready, "stale"));
            type.GetMethod("UpdateResult")!.Invoke(window, [stale]);
            Assert.Equal(28, window.Width);
            Assert.DoesNotContain("stale", Texts(window.Content));
            var ready = new TextTranslation(snapshot, settings.SettingsRevision, "v1", new("text", snapshot.Text, "en", ResultStatus.Ready, "I like apples."));
            type.GetMethod("UpdateResult")!.Invoke(window, [ready]);
            Assert.Equal(420, window.Width); Assert.Equal(180, window.Height);
            type.GetMethod("Collapse")!.Invoke(window, null);
            Assert.Equal(28, window.Width); Assert.DoesNotContain(snapshot.Text, Texts(window.Content));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Empty_text_followed_by_success_does_not_restore_the_previous_translation() => OnSta(() =>
    {
        var window = new TextOverlayWindow();
        try
        {
            var snapshot = new TextSnapshot("focus", 1, 10, "edit", TextMode.Sentence, "我喜欢苹果。");
            window.Present(snapshot, new("text", snapshot.Text, "en", ResultStatus.Ready, "I like apples."));
            window.Present(snapshot with { Version = 2, Text = "" }, null);
            window.PresentReadStatus(new(TextReadStatus.Success, 10, "edit", "focus", 2));
            Assert.Equal(28, window.Width); Assert.Equal("", Texts(window.Content));
            Assert.DoesNotContain("苹果", Texts(window.Content));
            Assert.DoesNotContain("I like apples", Texts(window.Content));
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public Task Empty_successful_output_does_not_expand_the_window(string gloss) => OnSta(() =>
    {
        var window = new TextOverlayWindow();
        try
        {
            var snapshot = new TextSnapshot("focus", 1, 10, "edit", TextMode.Sentence, "我喜欢苹果。");
            window.Present(snapshot, new("text", snapshot.Text, "en", ResultStatus.Ready, gloss));
            Assert.Equal(28, window.Width); Assert.Equal("", Texts(window.Content));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task New_text_collapses_until_its_own_successful_result_arrives() => OnSta(() =>
    {
        var window = new TextOverlayWindow();
        try
        {
            var first = new TextSnapshot("focus", 1, 10, "edit", TextMode.Sentence, "我喜欢苹果。");
            window.Present(first, new("text", first.Text, "en", ResultStatus.Ready, "I like apples."));
            var next = first with { Version = 2, Text = "今天的天气很好。" };
            window.Present(next, null);
            Assert.Equal(28, window.Width); Assert.Equal("", Texts(window.Content));
            Assert.Equal("翻译中…", window.StatusText);
            window.UpdateResult(new(first, 1, "v1", new("text", first.Text, "en", ResultStatus.Ready, "Old translation.")));
            Assert.Equal(28, window.Width);
            window.UpdateResult(new(next, 1, "v1", new("text", next.Text, "en", ResultStatus.Ready, "The weather is nice today.")));
            Assert.Equal(420, window.Width); Assert.Contains("The weather is nice today.", Texts(window.Content));
            Assert.DoesNotContain("I like apples.", Texts(window.Content));
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("translating", "翻译中…")]
    [InlineData("reading", "正在读取…")]
    [InlineData("composing", "输入中…")]
    [InlineData("empty", "等待输入")]
    public Task The_reason_for_collapsing_remains_available_after_focus_loss(string stage, string expected) => OnSta(() =>
    {
        var window = new TextOverlayWindow();
        try
        {
            var snapshot = new TextSnapshot("focus", 1, 10, "edit", TextMode.Sentence, stage == "empty" ? "" : "我喜欢苹果。");
            window.Present(snapshot, null);
            if (stage is "reading" or "composing")
                window.PresentReadStatus(new(stage == "reading" ? TextReadStatus.Reading : TextReadStatus.Composing, 10, "edit", "focus", 1));
            Assert.Equal(28, window.Width);
            window.Collapse();
            Assert.Equal(expected, window.LastNotice);
        }
        finally { window.Close(); }
    });

    private static string Texts(object? item) => item switch
    {
        System.Windows.Controls.TextBlock t => t.Text,
        System.Windows.Controls.Panel p => string.Join("\n", p.Children.Cast<object>().Select(Texts)),
        System.Windows.Controls.Decorator d => Texts(d.Child),
        System.Windows.Controls.ContentControl c => Texts(c.Content),
        _ => ""
    };
    private static Task OnSta(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); done.SetResult(); } catch (Exception e) { done.SetException(e); } finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return done.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
