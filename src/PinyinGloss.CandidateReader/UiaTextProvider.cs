using System.Runtime.InteropServices;
using PinyinGloss.Core;
using UIA = Interop.UIAutomationClient;

namespace PinyinGloss.CandidateReader;

internal sealed class UiaTextProvider(UIA.IUIAutomation automation) : ITextProvider
{
    public string Id => "UIA";
    public ProviderCaptureResult Capture(ResolvedTextTarget target, TextReadRequest request, CancellationToken token)
    {
        var focused = target.FocusedElement;
        if (focused == null) return new(TextReadStatus.NoSuitableProvider, null, Id);
        var provider = focused;
        var text = Pattern<UIA.IUIAutomationTextPattern>(focused, 10014);
        var owns = text != null;
        // 聊天中的只读 Group 可隔着多层布局借用文档接口，文字仍限定到原 Group 子范围。
        var maxDepth = request.Selection && focused.CurrentControlType == 50026 ? 64 : 8;
        for (var depth = 0; provider != null && depth < maxDepth; depth++)
        {
            if (provider.CurrentIsPassword != 0) return new(TextReadStatus.PasswordTarget, null, Id);
            if (depth > 0) text = Pattern<UIA.IUIAutomationTextPattern>(provider, 10014);
            if (text != null || provider.CurrentNativeWindowHandle == target.ForegroundWindow) break;
            provider = automation.RawViewWalker.GetParentElement(provider);
        }
        if (provider == null || text == null) return new(TextReadStatus.NoSuitableProvider, null, Id);
        UiaActiveTextScope scope;
        try { scope = UiaActiveTextScope.Create(text, focused, owns); }
        catch (Exception error) when (error is COMException or InvalidOperationException or ArgumentException && error.HResult != unchecked((int)0x80070005))
        { return new(TextReadStatus.InvalidTextScope, null, Id, "RangeFromChild", error.HResult); }
        var selections = request.Selection ? text.GetSelection() : null;
        if (selections?.Length > 1) return Result(TextReadStatus.MultipleSelections);
        if (selections?.Length == 1 && !Empty(selections.GetElement(0)))
        {
            var selection = scope.Normalize(selections.GetElement(0), IsFocusedInputRange);
            if (selection == null) return Result(TextReadStatus.InvalidTextScope);
            var value = ReadSelection(selection, scope.Range);
            if (value.Status != null) return Result(value.Status == "too_long" ? TextReadStatus.TooLong : value.Status == "target_changed" ? TextReadStatus.TargetChanged : TextReadStatus.InvalidTextScope);
            if (value.Text.Length > 0)
            {
                var capture = new CapturedText(value.Text, null, 0, value.Text.Length, false, false, ReadOnly(selection), false);
                return ValidAfter(null, false) ? Result(TextReadStatus.Success, capture) : Result(TextReadStatus.TargetChanged);
            }
        }
        if (!request.Sentence || focused.CurrentControlType == 50026) return Result(TextReadStatus.NoSelection);
        var originalCaret = Caret(provider, focused, text, out var active);
        if (originalCaret == null || !active) return Result(TextReadStatus.NoCaret);
        if (!Empty(originalCaret)) return Result(TextReadStatus.InvalidTextScope);
        var caret = scope.Normalize(originalCaret, IsFocusedInputRange);
        if (caret == null || !scope.Contains(caret, true)) return Result(TextReadStatus.InvalidTextScope);
        if (ReadOnly(caret)) return Result(TextReadStatus.NoCaret);
        // 浏览器可在提交后保留旧组合范围；以 reader 已验证的当前微软候选状态为准。
        if (request.Composing)
            return ValidAfter(originalCaret, active) ? Result(TextReadStatus.Composing, new("", 0, null, 0, true, true, false, true)) : Result(TextReadStatus.TargetChanged);
        token.ThrowIfCancellationRequested();
        var window = CaptureWindow(caret, scope.Range, false);
        return ValidAfter(originalCaret, active) ? Result(window.Status, window.Capture) : Result(TextReadStatus.TargetChanged);

        ProviderCaptureResult Result(TextReadStatus status, CapturedText? capture = null) => new(status, capture, Id, scope.Kind);
        bool IsFocusedInputRange(UIA.IUIAutomationTextRange range)
        {
            if (!owns || focused.CurrentControlType != 50004) return false;
            // contenteditable 的范围可归属文字子节点；遇到另一个编辑框即停止。
            var enclosing = range.GetEnclosingElement();
            for (var depth = 0; enclosing != null && depth < 8; depth++)
            {
                if (enclosing.CurrentIsPassword != 0) return false;
                if (automation.CompareElements(focused, enclosing) != 0) return true;
                if (enclosing.CurrentControlType == 50004) return false;
                enclosing = automation.RawViewWalker.GetParentElement(enclosing);
            }
            return false;
        }
        bool ValidAfter(UIA.IUIAutomationTextRange? originalCaret, bool originalActive)
        {
            if (scope.Range.Compare(UiaActiveTextScope.Create(text, focused, owns).Range) == 0) return false;
            if (selections != null)
            {
                var current = text.GetSelection();
                if (selections.Length != current.Length) return false;
                for (var i = 0; i < selections.Length; i++) if (selections.GetElement(i).Compare(current.GetElement(i)) == 0) return false;
            }
            if (originalCaret == null) return true;
            var currentCaret = Caret(provider, focused, text, out var currentActive);
            return currentActive == originalActive && currentCaret != null && originalCaret.Compare(currentCaret) != 0;
        }
    }
    internal static (CapturedText? Capture, TextReadStatus Status) CaptureWindow(UIA.IUIAutomationTextRange caret, UIA.IUIAutomationTextRange scope, bool readOnly)
    {
        const UIA.TextPatternRangeEndpoint start = UIA.TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start;
        const UIA.TextPatternRangeEndpoint end = UIA.TextPatternRangeEndpoint.TextPatternRangeEndpoint_End;
        if (!UiaActiveTextScope.Contains(caret, scope, true)) return (null, TextReadStatus.InvalidTextScope);
        var left = caret.Clone(); left.MoveEndpointByUnit(start, UIA.TextUnit.TextUnit_Character, -256);
        var right = caret.Clone(); right.MoveEndpointByUnit(end, UIA.TextUnit.TextUnit_Character, 257);
        if (left.CompareEndpoints(start, scope, start) < 0) left.MoveEndpointByRange(start, scope, start);
        if (right.CompareEndpoints(end, scope, end) > 0) right.MoveEndpointByRange(end, scope, end);
        var before = left.GetText(257);
        if (before.Length > 256) return (null, TextReadStatus.TooLong);
        var after = right.GetText(258);
        if (after.Length > 257) return (null, TextReadStatus.TooLong);
        var value = before + after;
        var confirmation = left.Clone(); confirmation.MoveEndpointByRange(end, right, end);
        if (!UiaActiveTextScope.Contains(confirmation, scope, false)) return (null, TextReadStatus.InvalidTextScope);
        if (confirmation.GetText(514) != value) return (null, TextReadStatus.TargetChanged);
        return (new(value, before.Length, null, 0, left.CompareEndpoints(start, scope, start) == 0,
            right.CompareEndpoints(end, scope, end) == 0, readOnly, false), TextReadStatus.Success);
    }
    internal static (string Text, string? Status) ReadSelection(UIA.IUIAutomationTextRange selection, UIA.IUIAutomationTextRange scope)
    {
        if (!UiaActiveTextScope.Contains(selection, scope, false)) return ("", "invalid_text_scope");
        var value = selection.GetText(257);
        if (value.Length > 256) return ("", "too_long");
        if (!UiaActiveTextScope.Contains(selection, scope, false)) return ("", "invalid_text_scope");
        return selection.GetText(257) != value ? ("", "target_changed") : (value.Trim(), null);
    }
    internal static T? Pattern<T>(UIA.IUIAutomationElement element, int id) where T : class
    { try { return element.GetCurrentPattern(id) as T; } catch (COMException error) when (error.HResult is unchecked((int)0x80004002) or unchecked((int)0x80004001) or unchecked((int)0x80040204)) { return null; } }
    private static bool Empty(UIA.IUIAutomationTextRange range) => range.CompareEndpoints(UIA.TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, range, UIA.TextPatternRangeEndpoint.TextPatternRangeEndpoint_End) == 0;
    private static bool ReadOnly(UIA.IUIAutomationTextRange range) => range.GetAttributeValue(40015) is not false;
    private static UIA.IUIAutomationTextRange? Caret(UIA.IUIAutomationElement provider, UIA.IUIAutomationElement focused, UIA.IUIAutomationTextPattern text, out bool active)
    {
        var pattern = Pattern<UIA.IUIAutomationTextPattern2>(provider, 10024);
        if (pattern != null) { var value = pattern.GetCaretRange(out var flag); active = flag != 0; return value; }
        var ranges = text.GetSelection(); active = focused.CurrentHasKeyboardFocus != 0;
        return active && ranges.Length == 1 && Empty(ranges.GetElement(0)) ? ranges.GetElement(0) : null;
    }
}
