using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using PinyinGloss.Core;

namespace PinyinGloss.Desktop;
internal sealed class ReaderSupervisor : IAsyncDisposable
{
    private CancellationTokenSource? _cancel; private Task? _loop;
    public event Action<ReaderMessage>? Message;
    public void Start(CaptureSettings settings)
    {
        _cancel = new(); _loop = RunAsync(settings, _cancel.Token);
    }
    private async Task RunAsync(CaptureSettings settings, CancellationToken token)
    {
        var restarts = new Queue<long>();
        while (!token.IsCancellationRequested)
        {
            while (restarts.Count > 0 && Stopwatch.GetElapsedTime(restarts.Peek()).TotalMinutes >= 1) restarts.Dequeue();
            if (restarts.Count >= 2)
            {
                Message?.Invoke(new(ReaderMessageKind.Status, Status: "读取器进入 30 秒冷却"));
                try { await Task.Delay(30000, token); } catch (OperationCanceledException) { break; }
                continue;
            }
            restarts.Enqueue(Stopwatch.GetTimestamp());
            var name = $"PinyinGloss-{Process.GetCurrentProcess().SessionId}-{Guid.NewGuid():N}";
            await using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var session = CancellationTokenSource.CreateLinkedTokenSource(token);
            using var job = new WindowsJob(); Process? process = null;
            try
            {
                var exe = Path.Combine(AppContext.BaseDirectory, "reader", "PinyinGloss.CandidateReader.exe");
                if (!File.Exists(exe)) throw new FileNotFoundException("缺少 reader/PinyinGloss.CandidateReader.exe，请使用完整发布包");
                var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
                foreach (var arg in new[] { "--pipe", name, "--poll-ms", settings.PollMs.ToString(), "--max-candidates", settings.MaxCandidates.ToString() }) info.ArgumentList.Add(arg);
                process = Process.Start(info) ?? throw new InvalidOperationException("读取器无法启动"); job.Add(process);
                await pipe.WaitForConnectionAsync(session.Token).WaitAsync(TimeSpan.FromSeconds(5), session.Token);
                if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid) || pid != process.Id) throw new InvalidDataException("IPC client identity mismatch");
                long lastMessage = Stopwatch.GetTimestamp(); long scanStart = 0;
                var watchdog = Task.Run(async () =>
                {
                    while (!session.IsCancellationRequested)
                    {
                        await Task.Delay(50, session.Token);
                        var scan = Interlocked.Read(ref scanStart);
                        if (scan != 0 && Stopwatch.GetElapsedTime(scan).TotalMilliseconds > settings.WatchdogMs ||
                            Stopwatch.GetElapsedTime(Interlocked.Read(ref lastMessage)).TotalSeconds > 5)
                        { Message?.Invoke(new(ReaderMessageKind.Hidden, Status: "UIA 看门狗超时")); session.Cancel(); break; }
                    }
                }, session.Token);
                try
                {
                    await PipeProtocol.PumpReaderAsync(pipe, message =>
                    {
                        Interlocked.Exchange(ref lastMessage, Stopwatch.GetTimestamp());
                        if (message.Kind == ReaderMessageKind.Heartbeat) Interlocked.Exchange(ref scanStart, message.Status == "scan-start" ? Stopwatch.GetTimestamp() : 0);
                        else Message?.Invoke(message);
                    }, session.Token);
                }
                finally { session.Cancel(); try { await watchdog; } catch (OperationCanceledException) { } }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or TimeoutException or OperationCanceledException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                Message?.Invoke(new(ReaderMessageKind.Hidden));
                if (!token.IsCancellationRequested) Message?.Invoke(new(ReaderMessageKind.Status, Status: "读取器: " + ex.GetType().Name));
            }
            finally { if (process != null) { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } process.Dispose(); } }
            try { await Task.Delay(1000, token); } catch (OperationCanceledException) { break; }
        }
    }
    public async ValueTask DisposeAsync()
    {
        _cancel?.Cancel(); if (_loop != null) { try { await _loop; } catch (OperationCanceledException) { } }
        _cancel?.Dispose();
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(nint pipe, out uint processId);
}
