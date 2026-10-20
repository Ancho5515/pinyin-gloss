using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PinyinGloss.Core;

namespace PinyinGloss.Providers;
public static class OutputValidator
{
    public static bool IsValid(string? s) => !string.IsNullOrWhiteSpace(s) && s.Length <= 1024 && s.Count(c => c == '\n') <= 4 &&
        !s.Contains("<think", StringComparison.OrdinalIgnoreCase) && !s.Contains("</think", StringComparison.OrdinalIgnoreCase) &&
        !s.Contains("```", StringComparison.Ordinal) && !s.Contains('\0');
}
public sealed class HttpModelProvider : ITranslationProvider, IDisposable, IAsyncDisposable
{
    private readonly ProviderSettings _config; private readonly ResponseSettings _response; private readonly HttpClient _client;
    private readonly ManagedLlamaServer? _managed; private readonly object _sync = new();
    private readonly CancellationTokenSource _lifetime = new(); private bool _disposed;
    private readonly Queue<DateTimeOffset> _starts = [];
    private Task? _loading; private DateTimeOffset _cooldown; private int _failures;
    public string State { get; private set; } = "未加载";
    public bool IsLoading { get { lock (_sync) return _loading is { IsCompleted: false }; } }
    public event Action? LoadCompleted;
    private string ReadyState => _managed?.ReadyState ?? "外部服务就绪";
    public DateTimeOffset LastUsed { get; private set; } = DateTimeOffset.UtcNow;
    public HttpModelProvider(ProviderSettings config, ResponseSettings response, HttpClient? client = null, int loadSeconds = 60, ManagedLlamaServer? managedServer = null)
    {
        _config = config; _response = response;
        // Disable redirects: credentials and candidate text must stay on the configured service.
        // 取消 SSE 时立即断开；默认 response drain 会继续读取旧输出、延迟后端感知取消。
        _client = client ?? new(new SocketsHttpHandler { AllowAutoRedirect = false, MaxResponseDrainSize = 0, ResponseDrainTimeout = TimeSpan.Zero });
        _client.BaseAddress = new Uri(config.Endpoint.TrimEnd('/') + "/"); _client.Timeout = Timeout.InfiniteTimeSpan;
        if (config.Managed) _managed = managedServer ?? new(config, loadSeconds);
    }
    public ProviderDescriptor Describe() => new(_config.Id, $"{_config.Model}:{_config.ModelRevision}:{_config.Quantization}:{_config.ModelSha256}", _config.Kind, _config.SupportedTargets, true, _managed != null);
    public Task StartAsync(CancellationToken token)
    {
        if (_disposed || !_config.Enabled || _managed == null) { State = _config.Enabled ? "外部服务" : "已禁用"; return Task.CompletedTask; }
        lock (_sync)
        {
            if (_managed.Ready) { State = ReadyState; return Task.CompletedTask; }
            if (_loading is { IsCompleted: false }) return _loading;
            if (DateTimeOffset.UtcNow < _cooldown) return Task.CompletedTask;
            while (_starts.Count > 0 && DateTimeOffset.UtcNow - _starts.Peek() > TimeSpan.FromMinutes(1)) _starts.Dequeue();
            if (_starts.Count >= 2) { _cooldown = DateTimeOffset.UtcNow.AddSeconds(30); State = "重启频率限制，冷却 30 秒"; return Task.CompletedTask; }
            _starts.Enqueue(DateTimeOffset.UtcNow);
            return _loading = LoadAsync(token);
        }
    }
    private async Task LoadAsync(CancellationToken token)
    {
        State = "正在加载";
        using var loadToken = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        try { await _managed!.StartAsync(loadToken.Token); State = ReadyState; }
        catch (OperationCanceledException) when (loadToken.IsCancellationRequested) { State = "加载已取消"; }
        catch (Exception ex) { State = "加载失败: " + ex.Message; _cooldown = DateTimeOffset.UtcNow.AddSeconds(30); }
        finally { LoadCompleted?.Invoke(); }
    }
    public async Task StopAsync(CancellationToken token)
    { if (_managed != null) await _managed.StopAsync(token); State = "已释放"; }
    public async Task<ProviderResult> QueryAsync(ProviderQuery q, CancellationToken token)
    {
        ProviderResult Result(ResultStatus status, string? gloss = null, string? error = null) => new(q.Candidate.Id, q.Candidate.Text, q.TargetLanguage, status, gloss, error);
        if (!_config.Enabled || !_config.SupportedTargets.Contains(q.TargetLanguage) || DateTimeOffset.UtcNow < _cooldown) return Result(ResultStatus.Unavailable);
        LastUsed = DateTimeOffset.UtcNow;
        if (_managed != null && !_managed.Ready) { _ = StartAsync(CancellationToken.None); return Result(ResultStatus.Unavailable, error: "model_loading"); }
        var apiKey = _managed?.ApiKey ?? (string.IsNullOrEmpty(_config.ApiKeyEnvironmentVariable) ? null : Environment.GetEnvironmentVariable(_config.ApiKeyEnvironmentVariable));
        if (!string.IsNullOrEmpty(_config.ApiKeyEnvironmentVariable) && string.IsNullOrEmpty(apiKey)) return Result(ResultStatus.Unavailable, error: "missing_credential");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(Math.Max(1, q.RemainingBudgetMs));
        var ct = budget.Token;
        var generationRequested = false; var finished = false;
        var maxOutput = q.MaxOutputTokens ?? _config.MaxOutputTokens;
        if (maxOutput is < 1 or > 128) return Result(ResultStatus.InvalidOutput, error: "output_token_budget");
        try
        {
            object payload; string route;
            if (_config.LlamaApi)
            {
                using var template = await PostJsonAsync("apply-template", new { messages = new[] { new { role = "user", content = q.Prompt } } }, apiKey, ct);
                var full = template.RootElement.GetProperty("prompt").GetString()!;
                using var count = await PostJsonAsync("tokenize", new { content = full, add_special = true, parse_special = true }, apiKey, ct);
                var tokens = count.RootElement.GetProperty("tokens").GetArrayLength();
                if (tokens > Math.Min(_config.InputTokenBudget, _config.ContextTokens - maxOutput - 16) || tokens + maxOutput + 16 > _config.ContextTokens) return Result(ResultStatus.InvalidOutput, error: "prompt_token_budget");
                payload = new { prompt = full, n_predict = maxOutput, temperature = 0, stream = true, cache_prompt = q.CachePrompt };
                route = "completion";
            }
            else
            {
                // No arbitrary local tokenizer can count an unknown vendor template. Enforce vendor errors and character bound, and report this limitation.
                if (q.Prompt.Length > 8192) return Result(ResultStatus.InvalidOutput, error: "prompt_size");
                payload = new { model = _config.Model, messages = new[] { new { role = "user", content = q.Prompt } }, temperature = 0, max_tokens = maxOutput, stream = true };
                route = "v1/chat/completions";
            }
            using var request = Request(route, payload, apiKey);
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var stallMs = _config.Kind == ProviderKind.OnlineModel ? _response.OnlineStallMs : _response.LocalStallMs;
            stall.CancelAfter(stallMs);
            generationRequested = true;
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(stall.Token);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var output = new StringBuilder();
            while (await reader.ReadLineAsync(stall.Token) is { } line)
            {
                if (line.Length > PipeProtocol.MaxFrameBytes) return Result(ResultStatus.InvalidOutput);
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var data = line[5..].Trim(); if (data == "[DONE]") { finished = true; break; }
                if (data.Length == 0) continue;
                using var message = JsonDocument.Parse(data); var root = message.RootElement;
                if (root.TryGetProperty("error", out _)) return Result(ResultStatus.Unavailable, error: "server_error");
                string? text = null;
                if (_config.LlamaApi)
                {
                    if (root.TryGetProperty("content", out var content)) text = content.GetString();
                    if (root.TryGetProperty("stop", out var stop) && stop.GetBoolean()) finished = true;
                    if (root.TryGetProperty("stop_type", out var stopType) && stopType.GetString() == "limit") return Result(ResultStatus.InvalidOutput, error: "output_truncated");
                    if (root.TryGetProperty("truncated", out var trunc) && trunc.GetBoolean()) return Result(ResultStatus.InvalidOutput, error: "context_truncated");
                }
                else if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];
                    if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String) text = content.GetString();
                    if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String)
                    { if (reason.GetString() == "length") return Result(ResultStatus.InvalidOutput, error: "output_truncated"); finished = true; }
                }
                if (!string.IsNullOrEmpty(text)) { output.Append(text); stall.CancelAfter(stallMs); }
                if (output.Length > 1024) return Result(ResultStatus.InvalidOutput);
                if (finished) break;
            }
            if (!finished || !OutputValidator.IsValid(output.ToString())) return Result(ResultStatus.InvalidOutput);
            _failures = 0; State = ReadyState; return Result(ResultStatus.Ready, output.ToString().Trim());
        }
        catch (OperationCanceledException)
        {
            if (token.IsCancellationRequested) throw;
            Fault(); return Result(ResultStatus.Timeout);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        { Fault(); return Result(ResultStatus.Unavailable, error: ex.GetType().Name); }
        finally
        {
            // 先释放 HTTP/SSE，再确认唯一槽位空闲；确认失败才释放 owned worker。
            // 分词前取消没有提交生成；调度器在本方法结束前保留生成信号量。
            if (_managed != null && generationRequested && !finished)
            {
                if (await ConfirmIdleAsync(apiKey)) { if (token.IsCancellationRequested) State = ReadyState; }
                else { await _managed.StopAsync(CancellationToken.None); State = "取消未确认，已释放"; }
            }
        }
    }
    private async Task<bool> ConfirmIdleAsync(string? apiKey)
    {
        using var grace = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        grace.CancelAfter(Math.Max(1, _response.CancelGraceMs));
        try
        {
            while (!grace.IsCancellationRequested)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "slots");
                request.Headers.Authorization = new("Bearer", apiKey);
                using var response = await _client.SendAsync(request, grace.Token);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(grace.Token);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: grace.Token);
                var slots = json.RootElement;
                if (slots.ValueKind != JsonValueKind.Array || slots.GetArrayLength() != 1) return false;
                if (!slots[0].GetProperty("is_processing").GetBoolean()) return true;
                await Task.Delay(25, grace.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException or ObjectDisposedException) { }
        return false;
    }
    private void Fault() { State = "服务异常"; if (++_failures >= 3) { _cooldown = DateTimeOffset.UtcNow.AddSeconds(30); _failures = 0; } }
    private HttpRequestMessage Request(string path, object payload, string? apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
        if (!string.IsNullOrEmpty(apiKey)) request.Headers.Authorization = new("Bearer", apiKey); return request;
    }
    private async Task<JsonDocument> PostJsonAsync(string path, object payload, string? apiKey, CancellationToken ct)
    {
        using var request = Request(path, payload, apiKey); using var response = await _client.SendAsync(request, ct); response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct); return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }
    public async ValueTask DisposeAsync()
    {
        Dispose();
        if (_loading != null) await _loading;
        if (_managed != null) await _managed.DisposeAsync();
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _lifetime.Cancel(); _managed?.Dispose(); _client.Dispose(); }
}
