using System.Threading.Channels;
using PinyinGloss.Core;

namespace PinyinGloss.Windows.Tests;

public class TextReadPublicationTests
{
    [Theory]
    [InlineData(TextReadStatus.Success)]
    [InlineData(TextReadStatus.NoCaret)]
    public void Scan_end_recovers_only_the_current_scan_result(TextReadStatus currentStatus)
    {
        var channel = Channel.CreateUnbounded<ReaderMessage>();
        InputFocus? focus = new("focus", 10, "edit", true);
        TextSnapshot? snapshot = new("focus", 1, 10, "edit", TextMode.Sentence, "旧句。");
        TextReadDiagnostic? diagnostic = new(TextReadStatus.Success, 10, "edit", "focus", 1);
        var current = new TextReadDiagnostic(currentStatus, 10, "edit", "focus", 2);
        var result = new TextReadResult(focus, currentStatus == TextReadStatus.Success ? snapshot with { Version = 2, Text = "新句。" } : null, current);
        var supervisorLastDiagnostic = diagnostic;
        PinyinGloss.CandidateReader.Program.PublishTextRead(channel.Writer, result, ref focus, ref snapshot, ref diagnostic);
        var scanEndSeen = false;
        while (channel.Reader.TryRead(out var message))
        {
            if (message.Kind == ReaderMessageKind.TextStatus) supervisorLastDiagnostic = message.TextDiagnostic;
            if (message.Kind == ReaderMessageKind.Heartbeat && message.Status == "scan-end")
            {
                scanEndSeen = true;
                Assert.Equal(2, supervisorLastDiagnostic!.Version);
                Assert.Equal(currentStatus, supervisorLastDiagnostic.Status);
            }
        }
        Assert.True(scanEndSeen);
    }
}
