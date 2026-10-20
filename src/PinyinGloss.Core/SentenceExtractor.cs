namespace PinyinGloss.Core;

public static class SentenceExtractor
{
    public static (TextMode? Mode, string Text, TextReadStatus Status) Extract(CapturedText capture, TextReadRequest request)
    {
        var text = capture.Text;
        if (text.Length > 514) return Failure(TextReadStatus.TooLong);
        if (text.Contains('\uFFFC')) return Failure(TextReadStatus.InvalidTextScope);
        if (capture.SelectionLength < 0) return Failure(TextReadStatus.InvalidTextScope);
        if (request.Selection && capture.SelectionLength > 0)
        {
            if (capture.SelectionStart is not { } selection || selection < 0 || selection > text.Length - capture.SelectionLength ||
                SplitsCharacter(selection) || SplitsCharacter(selection + capture.SelectionLength)) return Failure(TextReadStatus.InvalidTextScope);
            if (capture.SelectionLength > 256) return Failure(TextReadStatus.TooLong);
            var selected = text.Substring(selection, capture.SelectionLength).Trim();
            if (selected.Length > 0) return (TextMode.Selection, selected, TextReadStatus.Success);
        }
        if (!request.Sentence) return Failure(TextReadStatus.NoSelection);
        if (capture.ReadOnly || capture.CaretOffset == null) return Failure(TextReadStatus.NoCaret);
        var caret = capture.CaretOffset.Value;
        if (caret < 0 || caret > text.Length || SplitsCharacter(caret)) return Failure(TextReadStatus.InvalidTextScope);
        if (request.Composing || capture.Composing) return (TextMode.Sentence, "", TextReadStatus.Composing);
        var boundaries = new List<(int End, bool Newline)>();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') { boundaries.Add((i + 2, true)); i++; }
            else if (c is '\r' or '\n') boundaries.Add((i + 1, true));
            else if (c is '。' or '！' or '？' or '!' or '?' ||
                c == '.' && (i + 1 < text.Length ? char.IsWhiteSpace(text[i + 1]) : capture.AtScopeEnd)) boundaries.Add((i + 1, false));
        }
        var before = boundaries.FindLastIndex(b => b.End <= caret);
        var justEnded = before >= 0 && boundaries[before].End == caret;
        if (justEnded && boundaries[before].Newline) return (TextMode.Sentence, "", TextReadStatus.Success);
        var after = justEnded ? caret : boundaries.FirstOrDefault(b => b.End > caret).End;
        if (justEnded) before--;
        if (before < 0 && !capture.AtScopeStart || after == 0 && !capture.AtScopeEnd) return Failure(TextReadStatus.TooLong);
        var start = before < 0 ? 0 : boundaries[before].End;
        var end = after == 0 ? text.Length : after;
        if (end - start > 256) return Failure(TextReadStatus.TooLong);
        return (TextMode.Sentence, text[start..end].Trim(), TextReadStatus.Success);

        bool SplitsCharacter(int offset) => offset > 0 && offset < text.Length && char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]);
    }
    private static (TextMode?, string, TextReadStatus) Failure(TextReadStatus status) => (null, "", status);
}
