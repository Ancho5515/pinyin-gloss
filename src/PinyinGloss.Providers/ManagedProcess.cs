using System.Diagnostics;
using System.Text;
using PinyinGloss.Core;
namespace PinyinGloss.Providers;
public interface IManagedProcess : IDisposable
{
    bool HasExited { get; }
    int ExitCode { get; }
    string StartupLog => "";
    void FinishStartupCapture() { }
    void Kill();
}
internal sealed class ManagedProcess : IManagedProcess
{
    private readonly WindowsJob _job = new(); private readonly Process _process;
    private readonly object _logSync = new(); private readonly StringBuilder _startup = new(); private bool _capture = true;
    public ManagedProcess(ProcessStartInfo start)
    {
        _process = Process.Start(start) ?? throw new InvalidOperationException("模型进程未启动");
        try
        {
            _job.Add(_process);
            _process.OutputDataReceived += (_, e) => Capture(e.Data); _process.ErrorDataReceived += (_, e) => Capture(e.Data);
            _process.BeginOutputReadLine(); _process.BeginErrorReadLine();
        }
        catch { try { _process.Kill(true); } finally { _process.Dispose(); _job.Dispose(); } throw; }
    }
    public bool HasExited => _process.HasExited;
    public int ExitCode => _process.ExitCode;
    private void Capture(string? line)
    {
        if (line == null) return;
        lock (_logSync)
        {
            if (!_capture) return;
            // 仅保留加载期末尾的有界诊断；健康后停止，避免收集请求/候选文本。
            _startup.AppendLine(line.Length > 1024 ? line[..1024] : line);
            if (_startup.Length > 16384) _startup.Remove(0, _startup.Length - 16384);
        }
    }
    public string StartupLog { get { lock (_logSync) return _startup.ToString(); } }
    public void FinishStartupCapture() { lock (_logSync) _capture = false; }
    public void Kill() => _process.Kill(true);
    public void Dispose() { _process.Dispose(); _job.Dispose(); }
}
