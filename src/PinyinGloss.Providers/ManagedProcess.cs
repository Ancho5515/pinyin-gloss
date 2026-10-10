using System.Diagnostics;
using PinyinGloss.Core;
namespace PinyinGloss.Providers;
public interface IManagedProcess : IDisposable
{
    bool HasExited { get; }
    int ExitCode { get; }
    void Kill();
}
internal sealed class ManagedProcess : IManagedProcess
{
    private readonly WindowsJob _job = new(); private readonly Process _process;
    public ManagedProcess(ProcessStartInfo start)
    {
        _process = Process.Start(start) ?? throw new InvalidOperationException("模型进程未启动");
        try
        {
            _job.Add(_process);
            _process.OutputDataReceived += (_, _) => { }; _process.ErrorDataReceived += (_, _) => { };
            _process.BeginOutputReadLine(); _process.BeginErrorReadLine();
        }
        catch { try { _process.Kill(true); } finally { _process.Dispose(); _job.Dispose(); } throw; }
    }
    public bool HasExited => _process.HasExited;
    public int ExitCode => _process.ExitCode;
    public void Kill() => _process.Kill(true);
    public void Dispose() { _process.Dispose(); _job.Dispose(); }
}
