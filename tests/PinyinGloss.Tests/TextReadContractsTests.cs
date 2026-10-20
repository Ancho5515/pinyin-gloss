using System.Text.Json;
using PinyinGloss.Core;

namespace PinyinGloss.Tests;

public class TextReadContractsTests
{
    [Fact]
    public async Task A_reader_without_the_text_status_protocol_is_rejected()
    {
        using var stream = new MemoryStream();
        await PipeProtocol.WriteAsync(stream, new ReaderMessage(ReaderMessageKind.Focus, ProtocolVersion: 2), default);
        stream.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => PipeProtocol.PumpReaderAsync(stream, _ => { }, default));
    }

    [Fact]
    public void Read_failures_have_distinct_states_and_diagnostics_never_contain_exception_text()
    {
        var assembly = typeof(TextSnapshot).Assembly;
        var type = assembly.GetType("PinyinGloss.Core.TextReadDiagnostic");
        Assert.NotNull(type);
        var method = type.GetMethod("FromException");
        Assert.NotNull(method);
        var diagnostic = method.Invoke(null, [new UnauthorizedAccessException("synthetic private source text"), 10L, "UIA"]);
        var json = JsonSerializer.Serialize(diagnostic, SettingsJson.Options);
        Assert.DoesNotContain("synthetic private source text", json);
        Assert.DoesNotContain("Message", json, StringComparison.OrdinalIgnoreCase);
        var status = type.GetProperty("Status")!.GetValue(diagnostic)!.ToString();
        Assert.Equal("AccessDenied", status);
        var statusType = assembly.GetType("PinyinGloss.Core.TextReadStatus")!;
        Assert.NotEqual(Enum.Parse(statusType, "NoFocusedTarget"), Enum.Parse(statusType, "NoSuitableProvider"));
    }
}
