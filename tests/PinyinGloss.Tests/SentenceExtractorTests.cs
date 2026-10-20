using PinyinGloss.Core;

namespace PinyinGloss.Tests;

public class SentenceExtractorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Embedded_object_markers_are_not_treated_as_readable_body_text(bool selected)
    {
        var capture = new CapturedText("a\uFFFCb", selected ? null : 1, selected ? 0 : null, selected ? 3 : 0, true, true, false, false);
        Assert.Equal(TextReadStatus.InvalidTextScope, SentenceExtractor.Extract(capture, new(true, true, false)).Status);
    }
    [Theory]
    [InlineData("我喜欢苹果。今天的天气很好。", 14, "今天的天气很好。")]
    [InlineData("我喜欢苹果。今天的天气很好。", 3, "我喜欢苹果。")]
    [InlineData("First sentence. Second sentence.", 15, "First sentence.")]
    [InlineData("First sentence. Second sentence.", 20, "Second sentence.")]
    [InlineData("价格是3.14元。下一句。", 6, "价格是3.14元。")]
    [InlineData("网址 example.com/path。下一句。", 10, "网址 example.com/path。")]
    [InlineData("第一句。\r\n第二句。", 10, "第二句。")]
    [InlineData("第一句。\r\n", 6, "")]
    [InlineData("十角馆杀人", 0, "十角馆杀人")]
    [InlineData("十角馆杀人", 2, "十角馆杀人")]
    [InlineData("十角馆杀人", 5, "十角馆杀人")]
    [InlineData("苹果🍎很好。", 4, "苹果🍎很好。")]
    public void The_current_sentence_is_shared_by_every_provider(string text, int caret, string expected)
    {
        var result = Extract(new(text, caret, null, 0, true, true, false, false), new(true, true, false));
        Assert.Equal(TextReadStatus.Success, result.Status);
        Assert.Equal(expected, result.Text);
        Assert.Equal(TextMode.Sentence, result.Mode);
    }

    [Fact]
    public void Readonly_selection_is_prioritized_without_a_caret()
    {
        var result = Extract(new("第一句。\n第二句。", null, 0, 9, true, true, true, false), new(true, true, false));
        Assert.Equal(TextMode.Selection, result.Mode); Assert.Equal("第一句。\n第二句。", result.Text);
        Assert.Equal(TextReadStatus.Success, result.Status);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    [InlineData(3)] // surrogate pair 内部不能作为合法插入点。
    public void Invalid_caret_offsets_are_not_guessed(int caret)
    {
        var result = Extract(new("苹果🍎很好。", caret, null, 0, true, true, false, false), new(true, false, false));
        Assert.Equal(TextReadStatus.InvalidTextScope, result.Status); Assert.Equal("", result.Text);
    }

    [Theory]
    [InlineData(256, true, true, TextReadStatus.Success)]
    [InlineData(257, true, true, TextReadStatus.TooLong)]
    [InlineData(256, false, true, TextReadStatus.TooLong)]
    [InlineData(256, true, false, TextReadStatus.TooLong)]
    [InlineData(515, true, true, TextReadStatus.TooLong)]
    public void A_truncated_or_over_budget_sentence_is_never_translated(int length, bool atStart, bool atEnd, TextReadStatus expected)
    {
        var result = Extract(new(new string('甲', length), 128, null, 0, atStart, atEnd, false, false), new(true, false, false));
        Assert.Equal(expected, result.Status);
        if (expected != TextReadStatus.Success) Assert.Equal("", result.Text);
    }

    [Fact]
    public void Composition_and_missing_caret_have_distinct_results()
    {
        Assert.Equal(TextReadStatus.Composing, Extract(new("旧句。", 3, null, 0, true, true, false, true), new(true, true, false)).Status);
        Assert.Equal(TextReadStatus.NoCaret, Extract(new("旧句。", null, null, 0, true, true, false, false), new(true, true, false)).Status);
        Assert.Equal(TextReadStatus.NoSelection, Extract(new("旧句。", 3, null, 0, true, true, false, false), new(false, true, false)).Status);
    }

    private static (TextMode? Mode, string Text, TextReadStatus Status) Extract(CapturedText text, TextReadRequest request)
    {
        var type = typeof(TextSnapshot).Assembly.GetType("PinyinGloss.Core.SentenceExtractor");
        Assert.NotNull(type);
        return ((TextMode?, string, TextReadStatus))type.GetMethod("Extract")!.Invoke(null, [text, request])!;
    }
}
