using PinyinGloss.CandidateReader;
using PinyinGloss.Core;
using UIA = Interop.UIAutomationClient;

namespace PinyinGloss.Windows.Tests;

public class Ia2TextProviderTests
{
    [Fact]
    public async Task A_missing_legacy_accessible_object_does_not_terminate_the_reader()
    {
        var accessibleCalls = 0;
        var legacy = Stub<UIA.IUIAutomationLegacyIAccessiblePattern>((name, _) => name switch
        {
            "get_CurrentChildId" => 0,
            "GetIAccessible" => MissingAccessible(),
            _ => throw new NotSupportedException(name)
        });
        object? MissingAccessible() { accessibleCalls++; return null; }
        var element = Stub<UIA.IUIAutomationElement>((name, args) => name switch
        {
            "GetCurrentPattern" => (int)args![0]! == 10018 ? legacy : null,
            "get_CurrentIsPassword" => 0,
            "get_CurrentControlType" => 50026,
            "get_CurrentNativeWindowHandle" => (nint)10,
            _ => throw new NotSupportedException(name)
        });
        var automation = Stub<UIA.IUIAutomation>((name, _) => throw new NotSupportedException(name));
        var target = new ResolvedTextTarget(element, 10, 10, 12, "ChatGPT", "", "Chrome", 50026, "selection", false);
        var reader = new PinyinGloss.CandidateReader.TextReader(() => new(target, TextReadStatus.Success, 10), _ => true,
            new(new UiaTextProvider(automation), new Ia2TextProvider()));

        for (var i = 0; i < 2; i++)
        {
            var result = await reader.ReadAsync(new(true, true, false), default);
            Assert.Equal(TextReadStatus.NoSuitableProvider, result.Diagnostic.Status);
            Assert.Equal("IA2", result.Diagnostic.Provider);
            Assert.Null(result.Text);
        }
        Assert.Equal(2, accessibleCalls);
    }
    private static T Stub<T>(Func<string, object?[]?, object?> invoke) where T : class
    {
        var proxy = System.Reflection.DispatchProxy.Create<T, TextRangeTests.UiaStub>();
        ((TextRangeTests.UiaStub)(object)proxy).InvokeMember = invoke;
        return proxy;
    }
    [Theory]
    [InlineData(20, 3, 4, 30, TextReadStatus.InvalidTextScope)]
    [InlineData(20, 30, null, 0, TextReadStatus.InvalidTextScope)]
    [InlineData(300, 3, 0, 257, TextReadStatus.TooLong)]
    public void Invalid_offsets_are_rejected_before_requesting_text(int length, int caret, int? selection, int selected, TextReadStatus expected)
    {
        var reads = 0;
        var result = Read(length, caret, selection, selected, new(true, true, false), (_, _) => { reads++; return "unsafe"; });
        Assert.Equal(expected, result.Status); Assert.Equal(0, reads);
    }
    [Fact]
    public void Only_the_bounded_window_is_requested_from_the_active_object()
    {
        var requested = new List<(int, int)>();
        var result = Read(1000, 500, null, 0, new(true, false, false), (start, end) => { requested.Add((start, end)); return new string('甲', end - start); });
        Assert.Equal(TextReadStatus.Success, result.Status);
        Assert.Equal(2, requested.Count); Assert.All(requested, range => Assert.Equal((244, 758), range));
        Assert.Equal(256, result.Capture!.CaretOffset); Assert.Equal(514, result.Capture.Text.Length);
    }
    [Fact]
    public void An_embedded_object_or_unreliable_offset_unit_is_rejected()
    {
        Assert.Equal(TextReadStatus.InvalidTextScope, Read(3, 1, null, 0, new(true, false, false), (_, _) => "a\uFFFCb").Status);
        Assert.Equal(TextReadStatus.InvalidTextScope, Read(3, 1, null, 0, new(true, false, false), (_, _) => "wrong length").Status);
    }
    [Fact]
    public void A_same_object_same_length_document_change_is_rejected()
    {
        var reads = 0;
        var result = Read(3, 1, null, 0, new(true, false, false), (_, _) => ++reads == 1 ? "旧文。" : "新文。");
        Assert.Equal(TextReadStatus.TargetChanged, result.Status);
        Assert.Null(result.Capture);
    }
    private static ProviderCaptureResult Read(int length, int caret, int? start, int count, TextReadRequest request, Func<int, int, string> query)
    {
        var type = typeof(CandidateReadState).Assembly.GetType("PinyinGloss.CandidateReader.Ia2TextProvider");
        Assert.NotNull(type);
        return (ProviderCaptureResult)type.GetMethod("ReadBoundedText", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(null, [length, caret, start, count, false, request, query])!;
    }
}
