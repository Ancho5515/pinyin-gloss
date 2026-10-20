using PinyinGloss.CandidateReader;
using PinyinGloss.Core;
using ActiveTextReader = PinyinGloss.CandidateReader.TextReader;

namespace PinyinGloss.Windows.Tests;

public class TextReaderTests
{
    private static ResolvedTextTarget Target(string identity = "edit-a", bool password = false) =>
        new(null, 10, 11, 12, "test", "Edit", "test", 50004, identity, password);
    private sealed class Provider(ProviderCaptureResult result) : ITextProvider
    {
        public string Id => "UIA";
        public int Calls;
        public ProviderCaptureResult Result = result;
        public ProviderCaptureResult Capture(ResolvedTextTarget target, TextReadRequest request, CancellationToken token) { Calls++; return Result; }
    }
    private static Provider Ready(string text = "第一句。第二句。") => new(new(TextReadStatus.Success,
        new(text, text.Length, null, 0, true, true, false, false), "UIA", "OwnDocument"));

    [Fact]
    public async Task Disabled_reading_never_resolves_the_target()
    {
        var provider = Ready();
        var reader = new ActiveTextReader(() => throw new InvalidOperationException("must not resolve"), _ => true, new(provider));
        var result = await reader.ReadAsync(new(false, false, false), default);
        Assert.Null(result.Text); Assert.Equal(0, provider.Calls);
    }
    [Fact]
    public async Task A_password_target_is_rejected_before_any_provider_runs()
    {
        var provider = Ready(); var reader = new ActiveTextReader(() => new(Target(password: true), TextReadStatus.Success, 10), _ => true, new(provider));
        var result = await reader.ReadAsync(new(true, true, false), default);
        Assert.Equal(TextReadStatus.PasswordTarget, result.Diagnostic.Status); Assert.Equal(0, provider.Calls);
    }
    [Fact]
    public async Task A_target_changed_during_capture_does_not_publish_text()
    {
        var checks = 0; var provider = Ready();
        var reader = new ActiveTextReader(() => new(Target(), TextReadStatus.Success, 10), _ => ++checks == 1, new(provider));
        var result = await reader.ReadAsync(new(true, true, false), default);
        Assert.Null(result.Text); Assert.Null(result.Focus); Assert.Equal(TextReadStatus.TargetChanged, result.Diagnostic.Status);
    }
    [Fact]
    public async Task A_scope_rejection_does_not_try_another_provider()
    {
        var first = new Provider(new(TextReadStatus.InvalidTextScope, null, "UIA")); var second = Ready();
        var reader = new ActiveTextReader(() => new(Target(), TextReadStatus.Success, 10), _ => true, new(first, second));
        var result = await reader.ReadAsync(new(true, true, false), default);
        Assert.Null(result.Text); Assert.Equal(TextReadStatus.InvalidTextScope, result.Diagnostic.Status); Assert.Equal(0, second.Calls);
    }
    [Fact]
    public async Task Switching_editors_in_one_window_changes_the_session()
    {
        var target = Target(); var provider = Ready();
        var reader = new ActiveTextReader(() => new(target, TextReadStatus.Success, 10), _ => true, new(provider));
        var first = await reader.ReadAsync(new(true, true, false), default);
        target = Target("edit-b");
        var second = await reader.ReadAsync(new(true, true, false), default);
        Assert.NotEqual(first.Text!.SessionId, second.Text!.SessionId);
        Assert.Equal("第二句。", second.Text.Text);
    }
    [Fact]
    public async Task A_provider_cannot_report_success_without_a_capture()
    {
        var provider = new Provider(new(TextReadStatus.Success, null, "UIA"));
        var reader = new ActiveTextReader(() => new(Target(), TextReadStatus.Success, 10), _ => true, new(provider));
        var result = await reader.ReadAsync(new(true, true, false), default);
        Assert.Null(result.Text); Assert.Equal(TextReadStatus.UiaError, result.Diagnostic.Status);
    }
}
