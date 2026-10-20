using UIA = Interop.UIAutomationClient;

namespace PinyinGloss.CandidateReader;

internal sealed class UiaActiveTextScope(UIA.IUIAutomationTextRange range, string kind)
{
    internal UIA.IUIAutomationTextRange Range { get; } = range;
    internal string Kind { get; } = kind;
    internal static UiaActiveTextScope Create(UIA.IUIAutomationTextPattern text, UIA.IUIAutomationElement target, bool ownsPattern) =>
        new(ownsPattern ? text.DocumentRange : text.RangeFromChild(target), ownsPattern ? "OwnDocument" : "RangeFromChild");
    internal bool Contains(UIA.IUIAutomationTextRange value, bool requireCaret) => Contains(value, Range, requireCaret);
    internal UIA.IUIAutomationTextRange? Normalize(UIA.IUIAutomationTextRange value,
        Func<UIA.IUIAutomationTextRange, bool> belongsToInput)
    {
        if (Contains(value, false)) return value;
        if (Kind != "OwnDocument" || !belongsToInput(Range) || !belongsToInput(value)) return null;
        const UIA.TextPatternRangeEndpoint start = UIA.TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start;
        const UIA.TextPatternRangeEndpoint end = UIA.TextPatternRangeEndpoint.TextPatternRangeEndpoint_End;
        var normalized = value.Clone();
        // Chromium 可为同一输入框边界返回不同位置；只对齐经身份验证且没有文字的间隙。
        if (value.CompareEndpoints(start, Range, start) < 0)
        {
            var gap = value.Clone(); gap.MoveEndpointByRange(end, Range, start);
            if (!EmptyInputGap(gap)) return null;
            normalized.MoveEndpointByRange(start, Range, start);
        }
        if (value.CompareEndpoints(end, Range, end) > 0)
        {
            var gap = value.Clone(); gap.MoveEndpointByRange(start, Range, end);
            if (!EmptyInputGap(gap)) return null;
            normalized.MoveEndpointByRange(end, Range, end);
        }
        return Contains(normalized, false) ? normalized : null;

        bool EmptyInputGap(UIA.IUIAutomationTextRange gap) =>
            belongsToInput(gap) && gap.GetText(1).Length == 0 && gap.GetText(1).Length == 0;
    }
    internal static bool Contains(UIA.IUIAutomationTextRange value, UIA.IUIAutomationTextRange scope, bool requireCaret)
    {
        const UIA.TextPatternRangeEndpoint start = UIA.TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start;
        const UIA.TextPatternRangeEndpoint end = UIA.TextPatternRangeEndpoint.TextPatternRangeEndpoint_End;
        return value.CompareEndpoints(start, scope, start) >= 0 && value.CompareEndpoints(end, scope, end) <= 0 &&
            (!requireCaret || value.CompareEndpoints(start, value, end) == 0);
    }
}
