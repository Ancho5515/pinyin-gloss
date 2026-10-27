using System.IO.Pipes;
using System.Threading.Channels;
using PinyinGloss.Core;

namespace PinyinGloss.CandidateReader;
internal static class Program
{
    [MTAThread]
    private static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) return 2;
        var diagnose = args.Contains("--diagnose"); var includeText = args.Contains("--include-text");
        if (diagnose) Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        string? Value(string flag) { var index = Array.IndexOf(args, flag); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
        var pipeName = Value("--pipe");
        if (!diagnose && string.IsNullOrWhiteSpace(pipeName)) { Console.WriteLine("PinyinGloss.CandidateReader --diagnose [--include-text] | --pipe NAME"); return 2; }
        int Integer(string flag, int defaultValue, int min, int max) => int.TryParse(Value(flag), out var number) ? Math.Clamp(number, min, max) : defaultValue;
        var poll = Integer("--poll-ms", 50, 25, 1000); var max = Integer("--max-candidates", 10, 1, 10);
        var sentence = args.Contains("--sentence"); var selection = args.Contains("--selection");
        using var cancel = new CancellationTokenSource(); Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
        if (diagnose) { cancel.CancelAfter(TimeSpan.FromSeconds(Integer("--seconds", 30, 1, 300))); Console.WriteLine($"Pinyin Gloss UIA diagnostic | {Environment.OSVersion} | text={includeText}"); }
        var channel = Channel.CreateBounded<ReaderMessage>(new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        var thread = new Thread(() => Observe(channel.Writer, poll, max, sentence, selection, cancel.Token)) { IsBackground = true, Name = "UIA MTA" };
        thread.SetApartmentState(ApartmentState.MTA); thread.Start();
        try
        {
            using var pipe = diagnose ? null : new NamedPipeClientStream(".", pipeName!, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            if (pipe != null) await pipe.ConnectAsync(5000, cancel.Token);
            await foreach (var message in channel.Reader.ReadAllAsync(cancel.Token))
            {
                if (diagnose)
                {
                    if (message.Kind == ReaderMessageKind.Snapshot) Console.WriteLine($"snapshot={message.Snapshot!.Generation} count={message.Snapshot.Candidates.Count} " +
                        string.Join(" | ", message.Snapshot.Candidates.Select(c => $"{c.VisibleIndex}:" + (includeText ? c.Text : c.Id))));
                    else if (message.Kind == ReaderMessageKind.Text) Console.WriteLine(message.Text == null ? "text=cleared" : $"text={message.Text.Mode} status={message.Text.Status} length={message.Text.Text.Length}" + (includeText ? " " + message.Text.Text : ""));
                    else if (message.Kind == ReaderMessageKind.TextStatus) Console.WriteLine("text-status=" + System.Text.Json.JsonSerializer.Serialize(message.TextDiagnostic));
                    else if (message.Kind is ReaderMessageKind.Hidden or ReaderMessageKind.Status or ReaderMessageKind.Focus or ReaderMessageKind.CandidatesClosed) Console.WriteLine(message.Kind + ": " + message.Status);
                }
                else await PipeProtocol.WriteAsync(pipe!, message, cancel.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { return 1; }
        return 0;
    }
    private static void Observe(ChannelWriter<ReaderMessage> writer, int poll, int max, bool sentence, bool selection, CancellationToken token)
    {
        using var scanner = new UiaCandidateScanner(); var state = new CandidateReadState(); string? previousStatus = null; bool changed = true;
        var textReader = sentence || selection ? new TextReader() : null; InputFocus? previousFocus = null; TextSnapshot? previousText = null;
        TextReadDiagnostic? previousTextDiagnostic = null;
        writer.TryWrite(new(ReaderMessageKind.Status, Status: scanner.Diagnostics));
        Native.WinEventCallback callback = (_, evt, _, _, _, _, _) => changed = true;
        // OUTOFCONTEXT + SKIPOWNPROCESS. These observe UI visibility, never keyboard input.
        var foregroundHook = Native.SetWinEventHook(3, 3, 0, callback, 0, 0, 2);
        var objectHook = Native.SetWinEventHook(0x8002, 0x800C, 0, callback, 0, 0, 2);
        var next = DateTime.UtcNow;
        try
        {
            while (!token.IsCancellationRequested)
            {
                while (Native.PeekMessage(out var message, 0, 0, 0, 1)) { Native.TranslateMessage(ref message); Native.DispatchMessage(ref message); }
                if (!changed && DateTime.UtcNow < next) { token.WaitHandle.WaitOne(10); continue; }
                changed = false;
                writer.TryWrite(new(ReaderMessageKind.Heartbeat, Status: "scan-start"));
                var snapshot = scanner.Scan(max);
                var text = textReader?.ReadAsync(new(sentence, selection, snapshot != null || scanner.IsConfirmingPage), token).GetAwaiter().GetResult();
                PublishTextRead(writer, text, ref previousFocus, ref previousText, ref previousTextDiagnostic);
                // Page confirmation retains the previous display; only a real disappearance hides it.
                var update = state.Update(snapshot, scanner.IsConfirmingPage);
                if (update != null) writer.TryWrite(update.Kind == ReaderMessageKind.Hidden ? update with { Kind = ReaderMessageKind.CandidatesClosed } : update);
                if (previousStatus != scanner.Diagnostics) { previousStatus = scanner.Diagnostics; writer.TryWrite(new(ReaderMessageKind.Status, Status: previousStatus)); }
                next = DateTime.UtcNow.AddMilliseconds(snapshot != null || scanner.IsConfirmingPage || sentence || selection ? poll : 500);
            }
        }
        catch (Exception ex) { writer.TryWrite(new(ReaderMessageKind.Hidden, Status: ex.GetType().Name)); }
        finally { if (foregroundHook != 0) Native.UnhookWinEvent(foregroundHook); if (objectHook != 0) Native.UnhookWinEvent(objectHook); writer.TryComplete(); GC.KeepAlive(callback); }
    }
    internal static void PublishTextRead(ChannelWriter<ReaderMessage> writer, TextReadResult? text,
        ref InputFocus? previousFocus, ref TextSnapshot? previousText, ref TextReadDiagnostic? previousTextDiagnostic)
    {
        if (previousFocus != text?.Focus) { previousFocus = text?.Focus; writer.TryWrite(new(ReaderMessageKind.Focus, Focus: text?.Focus)); }
        if (previousText != text?.Text) { previousText = text?.Text; writer.TryWrite(new(ReaderMessageKind.Text, Text: text?.Text)); }
        if (text != null && previousTextDiagnostic != text.Diagnostic with { ElapsedMs = 0 })
        { previousTextDiagnostic = text.Diagnostic with { ElapsedMs = 0 }; writer.TryWrite(new(ReaderMessageKind.TextStatus, TextDiagnostic: text.Diagnostic)); }
        writer.TryWrite(new(ReaderMessageKind.Heartbeat, Status: "scan-end"));
    }
}
