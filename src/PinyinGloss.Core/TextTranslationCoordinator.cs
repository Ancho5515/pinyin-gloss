using System.Diagnostics;

namespace PinyinGloss.Core;

/// <summary>Only the latest active text is retained. Shares the candidate coordinator's model slot.</summary>
public sealed class TextTranslationCoordinator : IDisposable
{
    private readonly object _sync = new();
    private readonly AppSettings _settings;
    private readonly Func<TextSnapshot, CancellationToken, Task<ProviderResult>> _query;
    private readonly string _sourceVersion;
    private CancellationTokenSource? _request;
    private TextSnapshot? _latest;
    private long _epoch, _burstStarted;
    private bool _disposed, _waitingForModelLoad;
    public event Action<TextTranslation>? ResultUpdated;
    public TextTranslationCoordinator(AppSettings settings, Func<TextSnapshot, CancellationToken, Task<ProviderResult>> query, string sourceVersion)
    { _settings = SettingsJson.Clone(settings); _query = query; _sourceVersion = sourceVersion; }

    public void Submit(TextSnapshot snapshot)
    {
        lock (_sync)
        {
            if (_disposed || !_settings.Enabled || !(snapshot.Mode == TextMode.Selection ? _settings.TextTranslation.SelectionEnabled : _settings.TextTranslation.SentenceEnabled)) return;
            if (_latest == snapshot) return;
            var sameTarget = _latest?.SessionId == snapshot.SessionId && _latest.Mode == snapshot.Mode;
            _request?.Cancel(); _latest = snapshot; _waitingForModelLoad = false; var epoch = ++_epoch;
            if (!sameTarget || _burstStarted == 0) _burstStarted = Stopwatch.GetTimestamp();
            var delay = Math.Max(0, Math.Min(_settings.TextTranslation.StableMs, _settings.TextTranslation.MaxWaitMs - (int)Stopwatch.GetElapsedTime(_burstStarted).TotalMilliseconds));
            var request = _request = new();
            _ = RunAsync(snapshot, epoch, delay, request);
        }
    }
    private async Task RunAsync(TextSnapshot snapshot, long epoch, int delay, CancellationTokenSource request)
    {
        try
        {
            if (snapshot.Status != null || string.IsNullOrWhiteSpace(snapshot.Text)) return;
            await Task.Delay(delay, request.Token);
            lock (_sync) { if (_epoch != epoch) return; _burstStarted = 0; }
            request.CancelAfter(_settings.TextTranslation.DeadlineMs);
            var work = _query(snapshot, request.Token);
            _ = work.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
            var result = await work.WaitAsync(request.Token);
            if (result.OriginalText != snapshot.Text || result.TargetLanguage != _settings.TextTranslation.TargetLanguage) return;
            Publish(result);
        }
        catch (OperationCanceledException)
        { Publish(new($"{snapshot.SessionId}/{snapshot.Version}", snapshot.Text, _settings.TextTranslation.TargetLanguage, ResultStatus.Timeout)); }
        catch { Publish(new($"{snapshot.SessionId}/{snapshot.Version}", snapshot.Text, _settings.TextTranslation.TargetLanguage, ResultStatus.Unavailable)); }
        finally { lock (_sync) { if (ReferenceEquals(_request, request)) _request = null; request.Dispose(); } }
        void Publish(ProviderResult result)
        {
            lock (_sync)
                if (!_disposed && _epoch == epoch && _latest == snapshot)
                {
                    _waitingForModelLoad = result.ErrorCode == "model_loading";
                    ResultUpdated?.Invoke(new(snapshot, _settings.SettingsRevision, _sourceVersion, result));
                }
        }
    }
    public void RetryAfterModelLoad()
    {
        lock (_sync)
        {
            if (_disposed || !_waitingForModelLoad || _latest == null) return;
            var snapshot = _latest; _latest = null; _waitingForModelLoad = false;
            Submit(snapshot);
        }
    }
    public void Invalidate() { lock (_sync) { ++_epoch; _latest = null; _waitingForModelLoad = false; _burstStarted = 0; _request?.Cancel(); _request = null; } }
    public void Dispose() { lock (_sync) { if (_disposed) return; Invalidate(); _disposed = true; } }
}
