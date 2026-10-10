using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
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
            }
            if (!File.Exists(config.ExecutablePath) || !File.Exists(config.ModelPath)) throw new FileNotFoundException("需要 llama-server.exe 与 GGUF 文件");
            loading.Token.ThrowIfCancellationRequested();
            if (config.Model == "NiuTrans/LMT-60-1.7B") { using var gguf = File.OpenRead(config.ModelPath); GgufMetadata.ValidateQ8(gguf); }
            if (!string.IsNullOrEmpty(config.ModelSha256))
            {
                await using var file = File.OpenRead(config.ModelPath);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, loading.Token));
                if (!hash.Equals(config.ModelSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("GGUF SHA256 不匹配");
            }
            var endpoint = new Uri(config.Endpoint);
            var start = new ProcessStartInfo(Path.GetFullPath(config.ExecutablePath))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-m", Path.GetFullPath(config.ModelPath), "--host", endpoint.Host, "--port", endpoint.Port.ToString(), "-c", config.ContextTokens.ToString(), "-np", "1", "-ngl", config.UseGpu ? "99" : "0", "--cache-type-k", "f16", "--cache-type-v", "f16", "--jinja", "--reasoning-budget", "0" }) start.ArgumentList.Add(arg);
            start.Environment["LLAMA_API_KEY"] = ApiKey;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this); loading.Token.ThrowIfCancellationRequested();
                _process = processFactory?.Invoke(start) ?? new ManagedProcess(start);
            }
            using var client = new HttpClient { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(2) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            while (true)
            {
                loading.Token.ThrowIfCancellationRequested();
                lock (_sync)
                    if (_process == null || _process.HasExited) throw new InvalidOperationException($"模型退出: {_process?.ExitCode}");
                try
                {
                    bool healthy;
                    if (readyProbe != null) healthy = await readyProbe(loading.Token);
                    else { using var response = await client.GetAsync("health", loading.Token); healthy = response.IsSuccessStatusCode; }
                    if (healthy)
                    {
                        lock (_sync)
                        {
                            loading.Token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_disposed, this);
                            if (_process == null || _process.HasExited) throw new InvalidOperationException("模型在加载后退出");
                            _ready = true; return;
                        }
                    }
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) when (!loading.IsCancellationRequested) { }
                await Task.Delay(250, loading.Token);
            }
        }
        catch { lock (_sync) StopProcessLocked(); throw; }
        finally { lock (_sync) { if (ReferenceEquals(_loading, loading)) _loading = null; } _lifecycle.Release(); }
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
