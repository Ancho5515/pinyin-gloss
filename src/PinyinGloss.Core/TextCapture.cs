namespace PinyinGloss.Core;

public enum TextReadStatus
{
    Success, NoFocusedTarget, NoCaret, NoSelection, InvalidTextScope, UnsupportedControl,
    NoSuitableProvider, AccessDenied, Timeout, UiaError, Ia2Error, NativeProviderError,
    TooLong, MultipleSelections, Composing, PasswordTarget, TargetChanged, Reading
}
public record TextReadRequest(bool Sentence, bool Selection, bool Composing);
public record CapturedText(string Text, int? CaretOffset, int? SelectionStart, int SelectionLength,
    bool AtScopeStart, bool AtScopeEnd, bool ReadOnly, bool Composing);
public record TextReadResult(InputFocus? Focus, TextSnapshot? Text, TextReadDiagnostic Diagnostic);
public record TextReadDiagnostic(TextReadStatus Status, long ForegroundWindow, string? TargetIdentity = null,
    string? SessionId = null, long Version = 0, string? Provider = null, string? ScopeKind = null,
    string? ProcessName = null, string? ClassName = null, int? ControlType = null, int? ErrorCode = null, double ElapsedMs = 0)
{
    public static TextReadDiagnostic FromException(Exception error, long foregroundWindow, string provider) =>
        new(error is UnauthorizedAccessException || error.HResult == unchecked((int)0x80070005) ? TextReadStatus.AccessDenied :
            error is TimeoutException ? TextReadStatus.Timeout : provider == "UIA" ? TextReadStatus.UiaError :
            provider == "IA2" ? TextReadStatus.Ia2Error : TextReadStatus.NativeProviderError,
            foregroundWindow, Provider: provider, ErrorCode: error.HResult);
    public string Describe() => TextReadMessages.Describe(Status);
}
public static class TextReadMessages
{
    public static string Describe(TextReadStatus status) => status switch
    {
        TextReadStatus.Success => "正文已读取", TextReadStatus.NoFocusedTarget => "空闲",
        TextReadStatus.NoCaret => "无法确认活动光标", TextReadStatus.NoSelection => "没有活动选区",
        TextReadStatus.InvalidTextScope => "文本范围无效，已停止读取",
        TextReadStatus.UnsupportedControl => "当前控件不支持正文读取",
        TextReadStatus.NoSuitableProvider => "没有可用的文本读取接口",
        TextReadStatus.AccessDenied => "访问被拒绝", TextReadStatus.Timeout => "正文读取超时",
        TextReadStatus.UiaError => "UIA 正文读取失败", TextReadStatus.Ia2Error => "IA2 正文读取失败",
        TextReadStatus.NativeProviderError => "原生正文读取失败", TextReadStatus.TooLong => "文本过长",
        TextReadStatus.MultipleSelections => "不支持多个选区", TextReadStatus.Composing => "输入中…",
        TextReadStatus.PasswordTarget => "密码控件不读取", TextReadStatus.TargetChanged => "活动目标已改变",
        TextReadStatus.Reading => "正在读取…", _ => "无法读取正文"
    };
}
