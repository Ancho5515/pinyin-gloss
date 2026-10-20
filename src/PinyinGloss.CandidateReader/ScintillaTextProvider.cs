using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PinyinGloss.Core;

namespace PinyinGloss.CandidateReader;

internal sealed class ScintillaTextProvider : ITextProvider
{
    public string Id => "Scintilla";
    public ProviderCaptureResult Capture(ResolvedTextTarget target, TextReadRequest request, CancellationToken token)
    {
        var window = target.NativeFocusWindow;
        if (window == 0 || target.ClassName != "Scintilla" || Native.WindowClass(window) != "Scintilla") return Failure(TextReadStatus.NoSuitableProvider);
        Native.GetWindowThreadProcessId(window, out var pid);
        if (pid != target.ProcessId || Native.GetAncestor(window, 2).ToInt64() != target.ForegroundWindow) return Failure(TextReadStatus.InvalidTextScope);
        return ReadScalar((message, position) =>
        {
            if (Native.SendMessageTimeout(window, message, (nuint)position, 0, 3, 25, out var value) != 0) return (nint)value;
            var code = Marshal.GetLastWin32Error();
            if (code == 5) throw new UnauthorizedAccessException();
            throw new TimeoutException();
        }, request, token);
    }
    internal static ProviderCaptureResult ReadScalar(Func<uint, nint, nint> send, TextReadRequest request, CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var codePage = Query(2137); // SCI_GETCODEPAGE
            if (codePage != 65001) return Failure(TextReadStatus.UnsupportedControl);
            var length = Query(2006); var caret = Query(2008); var anchor = Query(2009);
            var selections = Query(2570); var readOnly = Query(2140) != 0;
            if (request.Selection && selections > 1) return Failure(TextReadStatus.MultipleSelections);
            if (length < 0 || caret < 0 || anchor < 0 || caret > length || anchor > length) return Failure(TextReadStatus.InvalidTextScope);
            var selected = request.Selection && caret != anchor;
            if (!selected && !request.Sentence) return Failure(TextReadStatus.NoSelection);
            if (!selected && readOnly) return Failure(TextReadStatus.NoCaret);
            if (!selected && request.Composing) return new(TextReadStatus.Composing, new("", 0, null, 0, true, true, false, true), "Scintilla", "FocusedHWND");
            var from = selected ? Math.Min(caret, anchor) : caret;
            var to = selected ? Math.Max(caret, anchor) : caret;
            if (selected)
            {
                var position = from; var units = 0;
                while (position < to)
                {
                    var next = Query(2418, position);
                    if (next <= position || next > to || next - position > 4) return Failure(TextReadStatus.InvalidTextScope);
                    units += next - position == 4 ? 2 : 1;
                    if (units > 256) return Failure(TextReadStatus.TooLong);
                    position = next;
                }
            }
            else
            {
                var units = 0;
                while (from > 0)
                {
                    var next = Query(2417, from);
                    if (next >= from || next < 0 || from - next > 4) return Failure(TextReadStatus.InvalidTextScope);
                    var width = from - next == 4 ? 2 : 1;
                    if (units + width > 256) break;
                    units += width;
                    from = next;
                }
                units = 0;
                while (to < length)
                {
                    var next = Query(2418, to);
                    if (next <= to || next > length || next - to > 4) return Failure(TextReadStatus.InvalidTextScope);
                    var width = next - to == 4 ? 2 : 1;
                    if (units + width > 258) break;
                    units += width;
                    to = next;
                }
            }
            if (to - from > (selected ? 1024 : 2056)) return Failure(TextReadStatus.TooLong);
            var bytes = new byte[(int)(to - from)];
            for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)(Query(2007, from + i) & 255);
            var utf8 = new UTF8Encoding(false, true);
            var value = utf8.GetString(bytes);
            var offset = selected ? 0 : utf8.GetCharCount(bytes, 0, (int)(caret - from));
            if (value.Length > (selected ? 256 : 514) || !selected && (offset > 256 || value.Length - offset > 258)) return Failure(TextReadStatus.TooLong);
            for (var i = 0; i < bytes.Length; i++)
                if ((byte)(Query(2007, from + i) & 255) != bytes[i]) return Failure(TextReadStatus.TargetChanged);
            if (Query(2006) != length || Query(2008) != caret || Query(2009) != anchor || Query(2570) != selections || Query(2137) != codePage || (Query(2140) != 0) != readOnly)
                return Failure(TextReadStatus.TargetChanged);
            return new(TextReadStatus.Success, new(value, selected ? null : offset, selected ? 0 : null,
                selected ? value.Length : 0, from == 0, to == length, readOnly, false), "Scintilla", "FocusedHWND");
        }
        catch (DecoderFallbackException) { return Failure(TextReadStatus.InvalidTextScope); }
        catch (TimeoutException) { return Failure(TextReadStatus.Timeout); }
        long Query(uint message, long value = 0)
        {
            token.ThrowIfCancellationRequested();
            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 175) throw new TimeoutException();
            var result = send(message, (nint)value).ToInt64();
            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds > 200) throw new TimeoutException();
            return result;
        }
    }
    private static ProviderCaptureResult Failure(TextReadStatus status) => new(status, null, "Scintilla", "FocusedHWND");
}
