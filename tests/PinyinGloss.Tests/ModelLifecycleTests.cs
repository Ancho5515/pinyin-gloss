using PinyinGloss.Core;
using PinyinGloss.Providers;
namespace PinyinGloss.Tests;
public class ModelLifecycleTests
{
    [Fact]
    public async Task Disposed_model_owner_cannot_launch_again()
    {
        using var files = new ModelFixture(); var process = new FakeModelProcess();
        using var server = new ManagedLlamaServer(files.Config, 5, _ => process, _ => Task.FromResult(true));
        server.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => server.StartAsync(default));
    }
    [Fact]
    public async Task Exited_model_is_no_longer_ready()
    {
        using var files = new ModelFixture(); var process = new FakeModelProcess();
        using var server = new ManagedLlamaServer(files.Config, 5, _ => process, _ => Task.FromResult(true));
        await server.StartAsync(default); Assert.True(server.Ready);
        process.Exited = true; Assert.False(server.Ready);
    }
    [Fact]
    public async Task Concurrent_disposal_has_one_process_owner()
    {
        using var files = new ModelFixture(); var process = new FakeModelProcess();
        using var server = new ManagedLlamaServer(files.Config, 5, _ => process, _ => Task.FromResult(true));
        await server.StartAsync(default); process.BlockFirstExitRead = true;
        var first = Task.Run(server.Dispose);
        await process.ReadBlocked.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = Task.Run(server.Dispose);
        await Task.WhenAny(second, Task.Delay(200)); // Release either implementation; assertion concerns ownership, not elapsed time.
        process.ReleaseRead.Set();
        await Task.WhenAll(first, second);
        Assert.Equal(1, process.DisposeCount);
    }
}
internal sealed class ModelFixture : IDisposable
{
    private readonly string _path = Path.GetTempFileName();
    public ProviderSettings Config { get; }
    public ModelFixture() => Config = new() { Model = "test-model", ExecutablePath = Environment.ProcessPath!, ModelPath = _path };
    public void Dispose() => File.Delete(_path);
}
internal sealed class FakeModelProcess : IManagedProcess
{
    public bool Exited; public bool BlockFirstExitRead; private int _reads; public int DisposeCount;
    public TaskCompletionSource ReadBlocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ManualResetEventSlim ReleaseRead { get; } = new();
    public bool HasExited
    {
        get { if (BlockFirstExitRead && Interlocked.Increment(ref _reads) == 1) { ReadBlocked.TrySetResult(); ReleaseRead.Wait(TimeSpan.FromSeconds(3)); } return Exited; }
    }
    public int ExitCode => 0;
    public void Kill() { }
    public void Dispose() => Interlocked.Increment(ref DisposeCount);
}
