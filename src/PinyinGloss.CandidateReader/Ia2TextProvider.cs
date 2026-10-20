using System.Runtime.InteropServices;
using PinyinGloss.Core;
using UIA = Interop.UIAutomationClient;

namespace PinyinGloss.CandidateReader;

internal sealed class Ia2TextProvider : ITextProvider
{
    public string Id => "IA2";
    public ProviderCaptureResult Capture(ResolvedTextTarget target, TextReadRequest request, CancellationToken token)
    {
        if (target.FocusedElement == null) return Failure(TextReadStatus.NoSuitableProvider);
        var legacy = UiaTextProvider.Pattern<UIA.IUIAutomationLegacyIAccessiblePattern>(target.FocusedElement, 10018);
        if (legacy == null) return Failure(TextReadStatus.NoSuitableProvider);
        if (legacy.CurrentChildId != 0) return Failure(TextReadStatus.InvalidTextScope);
        var accessible = legacy.GetIAccessible();
        if (accessible == null) return Failure(TextReadStatus.NoSuitableProvider);
        var role = Convert.ToInt32(accessible.get_accRole(0)); var state = Convert.ToInt32(accessible.get_accState(0));
        if ((state & 0x20000000) != 0) return Failure(TextReadStatus.PasswordTarget);
        if (role is not (42 or 15) || (state & 4) == 0) return Failure(TextReadStatus.NoSuitableProvider);
        if (accessible.accFocus is object focus && focus is not 0 && !SameObject(accessible, focus)) return Failure(TextReadStatus.InvalidTextScope);
        if (accessible is not IAccessibleServiceProvider services) return Failure(TextReadStatus.NoSuitableProvider);
        var service = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
        var iid = new Guid("E89F726E-C4F4-4C19-BB19-B647D7FA8478");
        var hr = services.QueryService(ref service, ref iid, out var instance);
        if (hr < 0) { if (hr == unchecked((int)0x80004002)) return Failure(TextReadStatus.NoSuitableProvider, hr); Marshal.ThrowExceptionForHR(hr); }
        if (instance == 0) return Failure(TextReadStatus.NoSuitableProvider);
        IIa2Text text;
        try
        {
            var textId = typeof(IIa2Text).GUID;
            hr = Marshal.QueryInterface(instance, in textId, out var pointer);
            if (hr < 0) { if (hr == unchecked((int)0x80004002)) return Failure(TextReadStatus.NoSuitableProvider, hr); Marshal.ThrowExceptionForHR(hr); }
            try { text = (IIa2Text)Marshal.GetObjectForIUnknown(pointer); }
            finally { if (pointer != 0) Marshal.Release(pointer); }
        }
        finally { Marshal.Release(instance); }
        Check(text.GetCharacterCount(out var length)); Check(text.GetSelectionCount(out var selections));
        if (selections > 1 && request.Selection) return Failure(TextReadStatus.MultipleSelections);
        var caretHr = text.GetCaretOffset(out var caret); Check(caretHr); if (caretHr != 0) caret = -1;
        int? selectionStart = null; var selected = 0; var selectionEnd = 0;
        if (request.Selection && selections == 1)
        {
            Check(text.GetSelection(0, out var start, out selectionEnd));
            selectionStart = Math.Min(start, selectionEnd); selected = Math.Abs(selectionEnd - start);
        }
        token.ThrowIfCancellationRequested();
        var result = ReadBoundedText(length, caret, selectionStart, selected, (state & 0x40) != 0 || role != 42, request, (start, end) =>
        { token.ThrowIfCancellationRequested(); Check(text.GetText(start, end, out var value)); return value ?? ""; });
        Check(text.GetCharacterCount(out var currentLength)); Check(text.GetSelectionCount(out var currentSelections));
        var currentCaretHr = text.GetCaretOffset(out var currentCaret); Check(currentCaretHr);
        if (currentLength != length || currentSelections != selections || currentCaretHr != caretHr || currentCaretHr == 0 && currentCaret != caret ||
            !SameObject(accessible, legacy.GetIAccessible()) || (Convert.ToInt32(accessible.get_accState(0)) & (4 | 0x20000000)) != 4)
            return Failure(TextReadStatus.TargetChanged);
        if (selectionStart != null)
        {
            Check(text.GetSelection(0, out var currentStart, out var currentEnd));
            if (Math.Min(currentStart, currentEnd) != selectionStart || Math.Abs(currentEnd - currentStart) != selected) return Failure(TextReadStatus.TargetChanged);
        }
        return result;
    }
    internal static ProviderCaptureResult ReadBoundedText(int length, int caret, int? selectionStart, int selected, bool readOnly,
        TextReadRequest request, Func<int, int, string> query)
    {
        if (length < 0 || selected < 0 || selectionStart is { } start && (start < 0 || start > length - selected)) return Failure(TextReadStatus.InvalidTextScope);
        var isSelection = request.Selection && selected > 0;
        if (isSelection && selectionStart == null) return Failure(TextReadStatus.InvalidTextScope);
        if (isSelection && selected > 256) return Failure(TextReadStatus.TooLong);
        if (!isSelection && !request.Sentence) return Failure(TextReadStatus.NoSelection);
        if (!isSelection && (caret < 0 || readOnly)) return Failure(TextReadStatus.NoCaret);
        if (!isSelection && caret > length) return Failure(TextReadStatus.InvalidTextScope);
        if (!isSelection && request.Composing) return new(TextReadStatus.Composing, new("", 0, null, 0, true, true, false, true), "IA2", "FocusedAccessible");
        var from = isSelection ? selectionStart!.Value : Math.Max(0, caret - 256);
        var to = isSelection ? from + selected : (int)Math.Min(length, (long)caret + 258);
        var value = query(from, to);
        if (value.Length != to - from || value.Contains('\uFFFC')) return Failure(TextReadStatus.InvalidTextScope);
        if (query(from, to) != value) return Failure(TextReadStatus.TargetChanged);
        return new(TextReadStatus.Success, new(value, isSelection ? null : caret - from, isSelection ? 0 : null,
            isSelection ? value.Length : 0, from == 0, to == length, readOnly, false), "IA2", "FocusedAccessible");
    }
    private static ProviderCaptureResult Failure(TextReadStatus status, int? code = null) => new(status, null, "IA2", "FocusedAccessible", code);
    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    private static bool SameObject(object left, object right)
    {
        if (!Marshal.IsComObject(left) || !Marshal.IsComObject(right)) return false;
        var a = Marshal.GetIUnknownForObject(left); var b = Marshal.GetIUnknownForObject(right);
        try { return a == b; } finally { Marshal.Release(a); Marshal.Release(b); }
    }
}
