using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using PinyinGloss.Core;

namespace PinyinGloss.Providers;
public sealed class ManagedLlamaServer(ProviderSettings config, int loadSeconds,
    Func<ProcessStartInfo, IManagedProcess>? processFactory = null,
    Func<CancellationToken, Task<bool>>? readyProbe = null) : IDisposable, IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _sync = new();
    private readonly CancellationTokenSource _shutdown = new();
    private CancellationTokenSource? _loading;
    private IManagedProcess? _process;
    private bool _ready, _disposed;
    public string? ActiveBackend { get; private set; }
    public int OffloadedLayers { get; private set; }
    public int TotalLayers { get; private set; }
    public string? FallbackReason { get; private set; }
    public string GpuStartupDiagnostics { get; private set; } = "";
    public string ReadyState => ActiveBackend == "Gpu" ? $"GPU 就绪（{OffloadedLayers}/{TotalLayers} 层）" :
        FallbackReason == null ? "CPU 就绪" : "CPU 就绪（GPU 回退：" + FallbackReason + "）";
    public string ApiKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public bool Ready
    {
        get
        {
            lock (_sync)
            {
                if (_disposed || !_ready || _process == null) return false;
                if (_process.HasExited) { _ready = false; return false; }
                return true;
            }
        }
    }
    public async Task StartAsync(CancellationToken token)
    {
        lock (_sync) ObjectDisposedException.ThrowIf(_disposed, this);
        using var loading = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        loading.CancelAfter(TimeSpan.FromSeconds(loadSeconds));
        await _lifecycle.WaitAsync(loading.Token);
        try
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (Ready) return;
                _loading = loading; StopProcessLocked();
                ActiveBackend = null; FallbackReason = null; GpuStartupDiagnostics = ""; OffloadedLayers = TotalLayers = 0;
            }
            if (!File.Exists(config.ExecutablePath) || !File.Exists(config.ModelPath)) throw new FileNotFoundException("需要 llama-server.exe 与 GGUF 文件");
            loading.Token.ThrowIfCancellationRequested();
            if (config.Model == "NiuTrans/LMT-60-1.7B") { using var gguf = File.OpenRead(config.ModelPath); GgufMetadata.ValidateQuantization(gguf, config.Quantization); }
            if (!string.IsNullOrEmpty(config.ModelSha256))
            {
                await using var file = File.OpenRead(config.ModelPath);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, loading.Token));
                if (!hash.Equals(config.ModelSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("GGUF SHA256 不匹配");
            }
            var cpuPath = string.IsNullOrWhiteSpace(config.CpuExecutablePath) ? config.ExecutablePath : config.CpuExecutablePath;
            if (!File.Exists(cpuPath)) throw new FileNotFoundException("CPU 备用运行时缺失");
            var endpoint = new Uri(config.Endpoint);
            if (config.UseGpu)
            {
                using var gpuLoading = CancellationTokenSource.CreateLinkedTokenSource(loading.Token);
                gpuLoading.CancelAfter(TimeSpan.FromSeconds(Math.Min(30, loadSeconds / 2.0)));
                try
                {
                    await StartAttemptAsync(config.ExecutablePath, true, endpoint, gpuLoading.Token); return;
                }
                catch (Exception ex) when (!loading.IsCancellationRequested &&
                    (ex is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException) && !IsPortFailure(ex.Message))
                {
                    lock (_sync)
                    {
                        GpuStartupDiagnostics = _process?.StartupLog ?? "";
                        FallbackReason = ex is OperationCanceledException ? "GPU 初始化超时" : ex.Message;
                        StopProcessLocked();
                    }
                }
            }
            // 与 GPU 尝试共用总加载期限与一次启动计数，外部取消不回退。
            loading.Token.ThrowIfCancellationRequested();
            await StartAttemptAsync(cpuPath, false, endpoint, loading.Token);
        }
        catch { lock (_sync) StopProcessLocked(); throw; }
        finally { lock (_sync) { if (ReferenceEquals(_loading, loading)) _loading = null; } _lifecycle.Release(); }
    }
    private async Task StartAttemptAsync(string executable, bool gpu, Uri endpoint, CancellationToken token)
    {
        var start = new ProcessStartInfo(Path.GetFullPath(executable))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        // fit 只调整未固定参数；上下文、精度、单槽不随显存下降。
        foreach (var arg in new[] { "-m", Path.GetFullPath(config.ModelPath), "--host", endpoint.Host, "--port", endpoint.Port.ToString(), "-c", config.ContextTokens.ToString(), "-np", "1", "-ngl", gpu ? "auto" : "0", "--fit", gpu ? "on" : "off", "--cache-type-k", "f16", "--cache-type-v", "f16", "--jinja", "--reasoning-budget", "0" }) start.ArgumentList.Add(arg);
        if (gpu) { start.ArgumentList.Add("--log-verbosity"); start.ArgumentList.Add("4"); }
        else { start.ArgumentList.Add("--device"); start.ArgumentList.Add("none"); }
        start.Environment["LLAMA_API_KEY"] = ApiKey;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this); token.ThrowIfCancellationRequested();
            _process = processFactory?.Invoke(start) ?? new ManagedProcess(start);
        }
        using var client = new HttpClient { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(2) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            lock (_sync)
                if (_process == null || _process.HasExited) throw new InvalidOperationException(ExitReason());
            try
            {
                bool healthy;
                if (readyProbe != null) healthy = await readyProbe(token);
                else { using var response = await client.GetAsync("health", token); healthy = response.IsSuccessStatusCode; }
                if (healthy)
                {
                    lock (_sync)
                    {
                        token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_disposed, this);
                        if (_process == null || _process.HasExited) throw new InvalidOperationException(ExitReason());
                        var match = Regex.Matches(_process.StartupLog, @"offloaded (\d+)/(\d+) layers to GPU").LastOrDefault();
                        if (gpu) GpuStartupDiagnostics = _process.StartupLog;
                        var layers = match != null ? int.Parse(match.Groups[1].Value) : 0;
                        if (gpu && layers == 0) throw new InvalidOperationException("未检测到实际 GPU 层卸载（无兼容设备或显存不足）");
                        _process.FinishStartupCapture();
                        ActiveBackend = gpu ? "Gpu" : "Cpu"; OffloadedLayers = gpu ? layers : 0;
                        TotalLayers = match != null ? int.Parse(match.Groups[2].Value) : 0;
                        _ready = true; return;
                    }
                }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!token.IsCancellationRequested) { }
            await Task.Delay(250, token);
        }
    }
    private static bool IsPortFailure(string message) => message.Contains("failed to bind", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("bind() failed", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("address already in use", StringComparison.OrdinalIgnoreCase);
    private string ExitReason()
    {
        var log = _process?.StartupLog ?? "";
        var reason = log.Split('\n').LastOrDefault(line => line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("failed", StringComparison.OrdinalIgnoreCase) || IsPortFailure(line))?.Trim();
        var exit = _process?.ExitCode;
        var description = exit == -1073741515 ? "运行时缺少依赖 DLL（0xC0000135）" : $"模型退出: {exit}";
        return description + (reason == null ? "" : "；" + reason[..Math.Min(300, reason.Length)]);
    }
    public async Task StopAsync(CancellationToken token)
    {
        lock (_sync) _loading?.Cancel();
        await _lifecycle.WaitAsync(token);
        try { lock (_sync) StopProcessLocked(); } finally { _lifecycle.Release(); }
    }
    private void StopProcessLocked()
    {
        _ready = false;
        var process = _process; _process = null;
        if (process == null) return;
        try { if (!process.HasExited) process.Kill(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        finally { process.Dispose(); }
    }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true; _shutdown.Cancel(); _loading?.Cancel(); StopProcessLocked();
        }
    }
    public async ValueTask DisposeAsync()
    {
        Dispose(); await _lifecycle.WaitAsync(); _lifecycle.Release();
    }
}
