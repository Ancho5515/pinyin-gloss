using System.Diagnostics;

namespace PinyinGloss.Core;

/// <summary>Latest-snapshot scheduler. It never touches the keyboard or input application.</summary>
public sealed class TranslationCoordinator : IDisposable
{
    private readonly object _sync = new();
    private readonly IReadOnlyDictionary<string, ITranslationProvider> _providers;
    private readonly Dictionary<string, SemaphoreSlim> _slots = [];
    private readonly Dictionary<string, ProviderResult> _cache = [];
    private readonly LinkedList<string> _cacheOrder = [];
    private readonly Dictionary<string, LinkedListNode<string>> _cacheNodes = [];
    private readonly HashSet<Task> _tasks = [];
    private AppSettings _settings;
    private CancellationTokenSource? _current;
    private long _epoch;
    private readonly Dictionary<string, int> _textWaiting = [];
    private bool _disposed;
    public TranslationCoordinator(IReadOnlyDictionary<string, ITranslationProvider> providers, AppSettings settings)
    {
        _providers = providers; _settings = SettingsJson.Clone(settings);
        foreach (var id in providers.Keys) _slots[id] = new(1, 1);
    }
    public event Action<TranslationCell>? CellUpdated;
    public void Submit(CandidateSnapshot snapshot)
    {
        lock (_sync)
        {
            if (_disposed) return;
            HideLocked();
            if (!_settings.Enabled || snapshot.Candidates.Count == 0) return;
            var epoch = _epoch; var settings = SettingsJson.Clone(_settings);
            var cancel = _current = new(); var started = Stopwatch.GetTimestamp();
            var task = Task.Run(() => RunAsync(snapshot, settings, epoch, started, cancel.Token));
            _tasks.Add(task);
            _ = task.ContinueWith(t => { lock (_sync) { _tasks.Remove(t); if (ReferenceEquals(_current, cancel)) _current = null; cancel.Dispose(); } _ = t.Exception; }, TaskScheduler.Default);
        }
    }
    public Task<ProviderResult> QueryManualAsync(ColumnSettings column, string word, CancellationToken token)
    {
        AppSettings settings;
        lock (_sync) { ObjectDisposedException.ThrowIf(_disposed, this); settings = SettingsJson.Clone(_settings); }
        return QueryAsync(column.SourceId, new("sample", word, 1), column, settings, 5000, token, manual: true);
    }
    public void Hide() { lock (_sync) HideLocked(); }
    public Task<ProviderResult> QueryTextAsync(TextSnapshot snapshot, CancellationToken token)
    {
        AppSettings settings;
        lock (_sync) { ObjectDisposedException.ThrowIf(_disposed, this); settings = SettingsJson.Clone(_settings); }
        var text = settings.TextTranslation;
        var candidate = new Candidate($"{snapshot.SessionId}/{snapshot.Version}", snapshot.Text, 1);
        if (!settings.Enabled || !(snapshot.Mode == TextMode.Sentence ? text.SentenceEnabled : text.SelectionEnabled) || snapshot.Text.Length is 0 or > 256 ||
            settings.Providers.FirstOrDefault(p => p.Id == text.SourceId)?.Kind != ProviderKind.LocalModel)
            return Task.FromResult(new ProviderResult(candidate.Id, candidate.Text, text.TargetLanguage, ResultStatus.Unavailable));
        return QueryAsync(text.SourceId, candidate, new() { Id = "text", SourceId = text.SourceId, TargetLanguage = text.TargetLanguage,
            PromptOverride = "Translate the following text from {{source_language_name}} into {{target_language_name}}:\n{{source_language_name}}: {{candidate}}\n{{target_language_name}}:" }, settings, text.DeadlineMs, token, text: true);
    }
    private void HideLocked() { _epoch++; _current?.Cancel(); _current = null; }
    public void UpdateSettings(AppSettings settings) { lock (_sync) { HideLocked(); _settings = SettingsJson.Clone(settings); _cache.Clear(); _cacheOrder.Clear(); _cacheNodes.Clear(); } }
    public async Task WhenIdleAsync() { Task[] tasks; lock (_sync) tasks = _tasks.ToArray(); await Task.WhenAll(tasks); }
    private async Task RunAsync(CandidateSnapshot snapshot, AppSettings settings, long epoch, long started, CancellationToken cancel)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(settings.Response.SnapshotDeadlineMs);
        var token = deadline.Token;
        int Remaining() => Math.Max(0, settings.Response.SnapshotDeadlineMs - (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        var columns = settings.Columns.Where(c => c.Enabled).ToArray();
        var candidates = snapshot.Candidates.Take(settings.Capture.MaxCandidates).OrderBy(c => c.Id == snapshot.SelectedCandidateId ? 0 : 1).ToArray();
        var completed = new HashSet<(string, string)>();
        var fallbacks = new Dictionary<(string, string), (string Source, ProviderResult Result)>();
        try
        {
            // Fast dictionaries first, before the stable-input delay. Never borrow another language's gloss.
            foreach (var candidate in candidates)
            foreach (var column in columns)
            {
                var primary = settings.Providers.First(p => p.Id == column.SourceId);
                IEnumerable<string> ids = primary.Kind == ProviderKind.LocalDictionary ? new[] { primary.Id }.Concat(column.FallbackSourceIds) : column.FallbackSourceIds;
                foreach (var id in ids)
                {
                    var result = await QueryAsync(id, candidate, column, settings, Remaining(), token);
                    if (result.Status != ResultStatus.Ready) continue;
                    fallbacks[(candidate.Id, column.Id)] = (id, result);
                    Publish(id, result, candidate, column); break;
                }
                if (primary.Kind == ProviderKind.LocalDictionary && !fallbacks.ContainsKey((candidate.Id, column.Id)))
                    Publish(primary.Id, new(candidate.Id, candidate.Text, column.TargetLanguage, ResultStatus.NotFound), candidate, column);
                else if (primary.Kind != ProviderKind.LocalDictionary && !fallbacks.ContainsKey((candidate.Id, column.Id)))
                {
                    var available = primary.Enabled && _providers.TryGetValue(primary.Id, out var provider) && provider.Describe().SupportedTargets.Contains(column.TargetLanguage);
                    Publish(primary.Id, new(candidate.Id, candidate.Text, column.TargetLanguage,
                        available ? ResultStatus.Pending : ResultStatus.Unavailable), candidate, column);
                }
            }
            if (columns.All(c => settings.Providers.First(p => p.Id == c.SourceId).Kind == ProviderKind.LocalDictionary)) return;
            var delay = settings.Response.StableMs - (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (delay > 0) await Task.Delay(delay, token);
            foreach (var candidate in candidates)
            foreach (var column in columns)
            {
                token.ThrowIfCancellationRequested();
                var primary = settings.Providers.First(p => p.Id == column.SourceId);
                if (primary.Kind == ProviderKind.LocalDictionary) continue;
                var result = await QueryAsync(primary.Id, candidate, column, settings, Remaining(), token);
                if (result.Status == ResultStatus.Ready || !fallbacks.ContainsKey((candidate.Id, column.Id))) Publish(primary.Id, result, candidate, column);
            }
        }
        catch (OperationCanceledException)
        {
            if (cancel.IsCancellationRequested) return;
            foreach (var candidate in candidates)
            foreach (var column in columns)
                if (!completed.Contains((candidate.Id, column.Id))) Publish(column.SourceId,
                    new(candidate.Id, candidate.Text, column.TargetLanguage, ResultStatus.Timeout), candidate, column);
        }
        void Publish(string source, ProviderResult result, Candidate candidate, ColumnSettings column)
        {
            if (result.CandidateId != candidate.Id || result.OriginalText != candidate.Text || result.TargetLanguage != column.TargetLanguage) return;
            lock (_sync)
            {
                if (_epoch != epoch || _disposed || cancel.IsCancellationRequested) return;
                var version = _providers.TryGetValue(source, out var provider) ? provider.Describe().Version : "unavailable";
                if (result.Status != ResultStatus.Pending) completed.Add((candidate.Id, column.Id));
                CellUpdated?.Invoke(new(snapshot.SnapshotId, snapshot.Generation, settings.SettingsRevision, column.Id,
                    column.TargetLanguage, source, version, result));
            }
        }
    }
    private async Task<ProviderResult> QueryAsync(string id, Candidate candidate, ColumnSettings column, AppSettings settings, int remaining, CancellationToken cancel, bool manual = false, bool text = false)
    {
        ProviderResult Status(ResultStatus status, string? error = null) => new(candidate.Id, candidate.Text, column.TargetLanguage, status, ErrorCode: error);
        var config = settings.Providers.First(p => p.Id == id);
        if (!config.Enabled || !_providers.TryGetValue(id, out var provider)) return Status(ResultStatus.Unavailable);
        var descriptor = provider.Describe();
        if (!descriptor.SupportedTargets.Contains(column.TargetLanguage)) return Status(ResultStatus.Unavailable, "unsupported_language");
        // LMT 的行式翻译模板会漏掉输入换行之后的内容；原始选区及结果身份保持不变。
        var promptText = text ? candidate.Text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ') : candidate.Text;
        var prompt = PromptTemplate.Render(column.PromptOverride ?? config.Prompt, promptText, column.TargetLanguage);
        var key = string.Join('\u001f', id, descriptor.Version, config.ModelRevision, config.Quantization, candidate.Text.Normalize(), column.TargetLanguage, prompt);
        if (!text) lock (_sync)
            if (_cache.TryGetValue(key, out var cached))
            { _cacheOrder.Remove(_cacheNodes[key]); _cacheNodes[key] = _cacheOrder.AddLast(key); return cached with { CandidateId = candidate.Id, OriginalText = candidate.Text }; }
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        if (remaining <= 0) return Status(ResultStatus.Timeout);
        limit.CancelAfter(manual ? settings.Resources.ModelLoadTimeoutSeconds * 1000 + remaining : remaining);
        var slot = descriptor.Kind == ProviderKind.LocalDictionary ? null : _slots[id];
        Task<ProviderResult>? work = null; bool acquired = false;
        if (text) lock (_sync) _textWaiting[id] = _textWaiting.GetValueOrDefault(id) + 1;
        try
        {
            if (!text && slot != null)
                while (true) { bool waiting; lock (_sync) waiting = _textWaiting.GetValueOrDefault(id) > 0; if (!waiting) break; await Task.Delay(10, limit.Token); }
            if (slot != null)
            {
                while (true)
                {
                    await slot.WaitAsync(limit.Token); acquired = true;
                    bool textPending; lock (_sync) textPending = !text && _textWaiting.GetValueOrDefault(id) > 0;
                    if (!textPending) break;
                    slot.Release(); acquired = false; await Task.Delay(10, limit.Token);
                }
            }
            if (manual) { await provider.StartAsync(limit.Token); limit.CancelAfter(remaining); }
            work = provider.QueryAsync(new(candidate, column.TargetLanguage, prompt, remaining, text ? 128 : null, !text), limit.Token);
            var timeout = descriptor.Kind == ProviderKind.LocalDictionary ? settings.Response.DictionaryTimeoutMs : remaining;
            var result = await work.WaitAsync(TimeSpan.FromMilliseconds(Math.Min(timeout, remaining)), limit.Token);
            if (result.CandidateId != candidate.Id || result.OriginalText != candidate.Text || result.TargetLanguage != column.TargetLanguage)
                return Status(ResultStatus.InvalidOutput, "identity_mismatch");
            if (result.Status == ResultStatus.Ready && (string.IsNullOrWhiteSpace(result.Gloss) || result.Gloss.Length > 1024)) return Status(ResultStatus.InvalidOutput);
            if (result.Status == ResultStatus.Ready && !text)
                lock (_sync)
                {
                    if (settings.Response.MemoryCacheEntries > 0 && !_cache.ContainsKey(key))
                    {
                        _cache[key] = result; _cacheNodes[key] = _cacheOrder.AddLast(key);
                        while (_cache.Count > settings.Response.MemoryCacheEntries)
                        { var oldest = _cacheOrder.First!.Value; _cacheOrder.RemoveFirst(); _cache.Remove(oldest); _cacheNodes.Remove(oldest); }
                    }
                }
            return result;
        }
        catch (OperationCanceledException) { cancel.ThrowIfCancellationRequested(); return Status(ResultStatus.Timeout); }
        catch (TimeoutException) { limit.Cancel(); return Status(ResultStatus.Timeout); }
        catch { return Status(ResultStatus.Unavailable, "provider_error"); }
        finally
        {
            if (text) lock (_sync) _textWaiting[id]--;
            // Retain the generation slot until the actual computation ends, even if it ignores cancellation.
            if (acquired && slot != null)
            {
                if (work != null && !work.IsCompleted) _ = work.ContinueWith(t => { _ = t.Exception; slot.Release(); }, TaskScheduler.Default);
                else slot.Release();
            }
            if (work != null) _ = work.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
        }
    }
    public void Dispose() { lock (_sync) { if (_disposed) return; HideLocked(); _disposed = true; _cache.Clear(); } }
}
