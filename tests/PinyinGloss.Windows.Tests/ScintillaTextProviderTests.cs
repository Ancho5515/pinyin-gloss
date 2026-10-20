using System.Text;
using PinyinGloss.CandidateReader;
using PinyinGloss.Core;

namespace PinyinGloss.Windows.Tests;

public class ScintillaTextProviderTests
{
    [Fact]
    public void Chinese_and_emoji_use_local_utf16_offsets_instead_of_bytes()
    {
        var document = new Document("第一句。苹果🍎很好。");
        var result = Read(document, new(true, true, false));
        Assert.Equal(TextReadStatus.Success, result.Status);
        var extracted = SentenceExtractor.Extract(result.Capture!, new(true, true, false));
        Assert.Equal("苹果🍎很好。", extracted.Text);
        Assert.Equal(11, result.Capture!.CaretOffset);
        Assert.All(document.Queries, q => Assert.DoesNotContain(q.Message, new uint[] { 2182, 2162, 2185, 2184 }));
    }
    [Fact]
    public void A_reverse_selection_keeps_the_entire_selected_text()
    {
        var document = new Document("前文。苹果🍎。后文。") { Caret = 9, Anchor = 19 };
        var result = Read(document, new(true, true, false));
        Assert.Equal(TextReadStatus.Success, result.Status);
        Assert.Equal("苹果🍎", SentenceExtractor.Extract(result.Capture!, new(true, true, false)).Text);
        Assert.Equal(TextMode.Selection, SentenceExtractor.Extract(result.Capture!, new(true, true, false)).Mode);
    }
    [Theory]
    [InlineData(936, 1, TextReadStatus.UnsupportedControl)]
    [InlineData(65001, 2, TextReadStatus.MultipleSelections)]
    public void Unsupported_encoding_and_multiple_selections_are_rejected_before_bytes(int codePage, int selections, TextReadStatus expected)
    {
        var document = new Document("苹果。") { CodePage = codePage, Selections = selections };
        Assert.Equal(expected, Read(document, new(true, true, false)).Status);
        Assert.DoesNotContain(document.Queries, q => q.Message == 2007);
    }
    [Fact]
    public void A_changed_caret_or_timeout_discards_every_byte()
    {
        var document = new Document("苹果。") { ChangeAfterBytes = true };
        Assert.Equal(TextReadStatus.TargetChanged, Read(document, new(true, true, false)).Status);
        document = new Document("苹果。") { Timeout = true };
        Assert.Equal(TextReadStatus.Timeout, Read(document, new(true, true, false)).Status);
    }
    [Fact]
    public void A_large_document_reads_only_the_bounded_window()
    {
        var document = new Document(new string('甲', 1000)) { Caret = 1500, Anchor = 1500 };
        var result = Read(document, new(true, false, false));
        Assert.Equal(TextReadStatus.Success, result.Status);
        Assert.InRange(result.Capture!.Text.Length, 1, 514);
        Assert.InRange(document.Queries.Where(q => q.Message == 2007).Select(q => q.Position).Distinct().Count(), 1, 2056);
        Assert.Equal(TextReadStatus.TooLong, SentenceExtractor.Extract(result.Capture, new(true, false, false)).Status);
    }
    [Fact]
    public void A_long_ascii_selection_is_rejected_before_reading_any_bytes()
    {
        var document = new Document(new string('a', 1024)) { Caret = 0, Anchor = 1024 };
        Assert.Equal(TextReadStatus.TooLong, Read(document, new(true, true, false)).Status);
        Assert.DoesNotContain(document.Queries, q => q.Message == 2007);
    }
    [Fact]
    public void Emoji_outside_a_short_current_sentence_do_not_exceed_the_utf16_window()
    {
        var prefix = string.Concat(Enumerable.Repeat("🍎", 260)) + "。";
        var document = new Document(prefix + "短句。" + string.Concat(Enumerable.Repeat("🍎", 260)))
        { Caret = Encoding.UTF8.GetByteCount(prefix + "短句"), Anchor = Encoding.UTF8.GetByteCount(prefix + "短句") };
        var result = Read(document, new(true, false, false));
        Assert.Equal(TextReadStatus.Success, result.Status);
        Assert.InRange(result.Capture!.Text.Length, 1, 514);
        Assert.Equal("短句。", SentenceExtractor.Extract(result.Capture, new(true, false, false)).Text);
    }
    [Fact]
    public void A_same_length_edit_during_byte_reading_does_not_publish_mixed_text()
    {
        var document = new Document("ABC.") { ReplacementAfterFirstByte = "XYZ." };
        var result = Read(document, new(true, false, false));
        Assert.Equal(TextReadStatus.TargetChanged, result.Status);
        Assert.Null(result.Capture);
    }
    private static ProviderCaptureResult Read(Document document, TextReadRequest request)
    {
        var type = typeof(CandidateReadState).Assembly.GetType("PinyinGloss.CandidateReader.ScintillaTextProvider");
        Assert.NotNull(type);
        return (ProviderCaptureResult)type.GetMethod("ReadScalar", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(null, [new Func<uint, nint, nint>(document.Query), request, CancellationToken.None])!;
    }
    private sealed class Document
    {
        private byte[] _bytes;
        private int _byteReads;
        public long Caret, Anchor; public int CodePage = 65001, Selections = 1;
        public bool ChangeAfterBytes, Timeout;
        public string? ReplacementAfterFirstByte;
        public List<(uint Message, long Position)> Queries { get; } = [];
        public Document(string value) { _bytes = Encoding.UTF8.GetBytes(value); Caret = Anchor = _bytes.Length; }
        public nint Query(uint message, nint value)
        {
            if (Timeout) throw new TimeoutException();
            var position = value.ToInt64(); Queries.Add((message, position));
            if (message == 2007 && ++_byteReads == 2 && ReplacementAfterFirstByte != null)
                _bytes = Encoding.UTF8.GetBytes(ReplacementAfterFirstByte);
            return (nint)(message switch
            {
                2137 => CodePage, 2006 => _bytes.Length, 2008 => Caret + (ChangeAfterBytes && Queries.Any(q => q.Message == 2007) ? -3 : 0),
                2009 => Anchor, 2570 => Selections, 2140 => 0,
                2007 => position >= 0 && position < _bytes.Length ? _bytes[position] : 0,
                2417 => Previous((int)position), 2418 => Next((int)position),
                _ => throw new InvalidOperationException("unexpected message")
            });
        }
        private int Previous(int position) { if (position == 0) return 0; position--; while (position > 0 && (_bytes[position] & 0xc0) == 0x80) position--; return position; }
        private int Next(int position) { if (position >= _bytes.Length) return _bytes.Length; position++; while (position < _bytes.Length && (_bytes[position] & 0xc0) == 0x80) position++; return position; }
    }
}
