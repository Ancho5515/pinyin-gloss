using PinyinGloss.Core;
using PinyinGloss.Providers;
namespace PinyinGloss.Tests;
public class ModelLifecycleTests
{
    [Fact]
    public async Task Gpu_initialization_failure_falls_back_once_and_reports_cpu()
    {
        using var files = new ModelFixture(); var attempts = new List<string[]>();
        var failed = new FakeModelProcess { Exited = true, StartupLog = "ggml_cuda_init: CUDA error: no CUDA-capable device" };
        var cpu = new FakeModelProcess();
        var config = files.Config with { UseGpu = true, Managed = true };
        var server = new ManagedLlamaServer(config, 5, start => { attempts.Add(start.ArgumentList.ToArray()); return attempts.Count == 1 ? failed : cpu; }, _ => Task.FromResult(true));
        using var provider = new HttpModelProvider(config, new(), managedServer: server);
        await provider.StartAsync(default);
        Assert.True(server.Ready); Assert.Equal(2, attempts.Count); Assert.Equal(1, failed.DisposeCount);
        Assert.Contains("auto", attempts[0]); Assert.Contains("0", attempts[1]); Assert.Contains("none", attempts[1]);
        Assert.Contains("CPU", provider.State); Assert.Contains("回退", provider.State);
        await provider.StartAsync(default); Assert.Equal(2, attempts.Count); // 一次加载流程，不反复重试 GPU。
    }
    [Fact]
    public async Task Gpu_health_without_offload_is_not_reported_as_gpu()
    {
        using var files = new ModelFixture(); var attempts = 0;
        var config = files.Config with { UseGpu = true, Managed = true };
        var server = new ManagedLlamaServer(config, 5, _ => { attempts++; return new FakeModelProcess { StartupLog = "offloaded 0/29 layers to GPU" }; }, _ => Task.FromResult(true));
        using var provider = new HttpModelProvider(config, new(), managedServer: server);
        await provider.StartAsync(default);
        Assert.Equal(2, attempts); Assert.Contains("CPU", provider.State); Assert.Contains("回退", provider.State);
    }
    [Fact]
    public async Task Real_offload_reports_gpu_and_does_not_start_cpu()
    {
        using var files = new ModelFixture(); var attempts = 0;
        var config = files.Config with { UseGpu = true, Managed = true };
        var server = new ManagedLlamaServer(config, 5, _ => { attempts++; return new FakeModelProcess { StartupLog = "load_tensors: offloaded 12/29 layers to GPU" }; }, _ => Task.FromResult(true));
        using var provider = new HttpModelProvider(config, new(), managedServer: server);
        await provider.StartAsync(default);
        Assert.True(server.Ready); Assert.Equal(1, attempts); Assert.Contains("GPU", provider.State); Assert.Contains("12/29", provider.State);
    }
    [Fact]
    public async Task Missing_gpu_dependency_reports_readable_fallback_reason()
    {
        using var files = new ModelFixture(); var attempts = 0;
        var config = files.Config with { UseGpu = true, Managed = true };
        var server = new ManagedLlamaServer(config, 5, _ => ++attempts == 1 ? new FakeModelProcess { Exited = true, Code = -1073741515 } : new FakeModelProcess(), _ => Task.FromResult(true));
        using var provider = new HttpModelProvider(config, new(), managedServer: server);
        await provider.StartAsync(default); Assert.True(server.Ready); Assert.Contains("缺少依赖 DLL", provider.State);
    }
    [Fact]
    public async Task Gpu_launch_requests_backend_loading_diagnostics()
    {
        using var files = new ModelFixture(); string[]? arguments = null;
        using var server = new ManagedLlamaServer(files.Config with { UseGpu = true }, 5,
            start => { arguments = start.ArgumentList.ToArray(); return new FakeModelProcess { StartupLog = "offloaded 29/29 layers to GPU" }; }, _ => Task.FromResult(true));
        await server.StartAsync(default);
        Assert.Contains("--log-verbosity", arguments!); Assert.Contains("4", arguments!);
    }
    [Fact]
    public async Task Port_conflict_is_not_retried_as_a_gpu_failure()
    {
        using var files = new ModelFixture(); var attempts = 0;
        using var server = new ManagedLlamaServer(files.Config with { UseGpu = true }, 5,
            _ => { attempts++; return new FakeModelProcess { Exited = true, StartupLog = "error: failed to bind server socket: address already in use" }; }, _ => Task.FromResult(true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => server.StartAsync(default)); Assert.Equal(1, attempts);
    }
    [Fact]
    public async Task Cpu_fallback_failure_stops_after_two_attempts()
    {
        using var files = new ModelFixture(); var attempts = 0;
        using var server = new ManagedLlamaServer(files.Config with { UseGpu = true }, 5,
            _ => { attempts++; return new FakeModelProcess { Exited = true, StartupLog = "error: failed to initialize backend" }; }, _ => Task.FromResult(true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => server.StartAsync(default)); Assert.Equal(2, attempts); Assert.False(server.Ready);
    }
    [Fact]
    public async Task Cpu_only_uses_the_independent_backup_runtime()
    {
        using var files = new ModelFixture(); string? launched = null;
        using var server = new ManagedLlamaServer(files.Config with { UseGpu = false, CpuExecutablePath = files.Config.ModelPath }, 5,
            start => { launched = start.FileName; return new FakeModelProcess(); }, _ => Task.FromResult(true));
        await server.StartAsync(default); Assert.Equal(files.Config.ModelPath, launched); Assert.Equal("Cpu", server.ActiveBackend);
    }
    [Fact]
    public async Task Cancelled_gpu_loading_does_not_start_cpu()
    {
        using var files = new ModelFixture(); var attempts = 0; using var cancellation = new CancellationTokenSource();
        using var server = new ManagedLlamaServer(files.Config with { UseGpu = true }, 5,
            _ => { attempts++; return new FakeModelProcess(); }, _ => { cancellation.Cancel(); return Task.FromResult(false); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => server.StartAsync(cancellation.Token));
        Assert.Equal(1, attempts); Assert.False(server.Ready);
    }
    [Fact]
    public async Task Model_hash_failure_does_not_attempt_any_device()
    {
        using var files = new ModelFixture(); var attempts = 0;
        using var server = new ManagedLlamaServer(files.Config with { UseGpu = true, ModelSha256 = new string('a', 64) }, 5,
            _ => { attempts++; return new FakeModelProcess(); }, _ => Task.FromResult(true));
        await Assert.ThrowsAsync<InvalidDataException>(() => server.StartAsync(default)); Assert.Equal(0, attempts);
    }
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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_keeps_idle_worker_but_retires_unconfirmed_worker(bool busy)
    {
        using var files = new ModelFixture(); var process = new FakeModelProcess();
        var config = files.Config with { Managed = true };
        var server = new ManagedLlamaServer(config, 5, _ => process, _ => Task.FromResult(true));
        var handler = new CancelHandler(busy);
        using var provider = new HttpModelProvider(config, new() { CancelGraceMs = 100 }, new HttpClient(handler), managedServer: server);
        await provider.StartAsync(default);
        using var cancellation = new CancellationTokenSource();
        var task = provider.QueryAsync(new(new("1", "苹果", 1), "en", "Translate", 3000), cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(!busy, server.Ready);
        Assert.Equal(busy ? 1 : 0, process.DisposeCount);
        Assert.Contains("/slots", handler.Paths);
    }

    [Fact]
    public async Task Confirmed_idle_after_stall_preserves_failure_status()
    {
        using var files = new ModelFixture(); var process = new FakeModelProcess();
        var config = files.Config with { Managed = true };
        var server = new ManagedLlamaServer(config, 5, _ => process, _ => Task.FromResult(true));
        using var provider = new HttpModelProvider(config, new() { LocalStallMs = 50, CancelGraceMs = 100 }, new HttpClient(new CancelHandler(false)), managedServer: server);
        await provider.StartAsync(default);
        var result = await provider.QueryAsync(new(new("1", "苹果", 1), "en", "Translate", 3000), default);
        Assert.Equal(ResultStatus.Timeout, result.Status); Assert.True(server.Ready);
        Assert.Equal("服务异常", provider.State);
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
internal sealed class CancelHandler(bool busy) : HttpMessageHandler
{
    internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal List<string> Paths { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath; Paths.Add(path);
        if (path == "/completion") { Started.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellationToken); }
        var json = path switch { "/apply-template" => "{\"prompt\":\"template\"}", "/tokenize" => "{\"tokens\":[1,2]}",
            "/slots" => "[{\"is_processing\":" + (busy ? "true" : "false") + "}]", _ => "{}" };
        return new(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) };
    }
}
internal sealed class ModelFixture : IDisposable
{
    private readonly string _path = Path.GetTempFileName();
    public ProviderSettings Config { get; }
    public ModelFixture() => Config = new() { Model = "test-model", ExecutablePath = Environment.ProcessPath!, ModelPath = _path, UseGpu = false };
    public void Dispose() => File.Delete(_path);
}
internal sealed class FakeModelProcess : IManagedProcess
{
    public string StartupLog { get; set; } = "";
    public bool Exited; public bool BlockFirstExitRead; private int _reads; public int DisposeCount;
    public TaskCompletionSource ReadBlocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ManualResetEventSlim ReleaseRead { get; } = new();
    public bool HasExited
    {
        get { if (BlockFirstExitRead && Interlocked.Increment(ref _reads) == 1) { ReadBlocked.TrySetResult(); ReleaseRead.Wait(TimeSpan.FromSeconds(3)); } return Exited; }
    }
    public int Code; public int ExitCode => Code;
    public void Kill() { }
    public void Dispose() => Interlocked.Increment(ref DisposeCount);
}
