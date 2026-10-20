using System.Diagnostics;
using System.Runtime.InteropServices;
using PinyinGloss.Core;

namespace PinyinGloss.CandidateReader;

internal sealed class TextReader
{
    private readonly Func<TextReadRequest, TextTargetResolution> _resolve;
    private readonly Func<ResolvedTextTarget, bool> _isCurrent;
    private readonly TextProviderRouter _router;
    private string? _identity;
    private string _session = Guid.NewGuid().ToString("N");
    private long _version;
    private TextSnapshot? _last;
    internal TextReader()
    {
        var resolver = new TextTargetResolver(); _resolve = resolver.Resolve; _isCurrent = resolver.IsCurrent;
        _router = new(new UiaTextProvider(resolver.Automation), new ScintillaTextProvider(), new Ia2TextProvider());
    }
    internal TextReader(Func<TextTargetResolution> resolve, Func<ResolvedTextTarget, bool> isCurrent, TextProviderRouter router)
    { _resolve = _ => resolve(); _isCurrent = isCurrent; _router = router; }
    internal ValueTask<TextReadResult> ReadAsync(TextReadRequest request, CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        TextTargetResolution? resolution = null; ProviderCaptureResult? capture = null;
        TextReadResult result;
        try
        {
            token.ThrowIfCancellationRequested();
            if (!request.Sentence && !request.Selection) return ValueTask.FromResult(Failure(TextReadStatus.NoFocusedTarget));
            resolution = _resolve(request);
            if (resolution.Target is not { } target) return ValueTask.FromResult(Failure(resolution.Status));
            if (target.Password) return ValueTask.FromResult(Failure(TextReadStatus.PasswordTarget));
            if (!_isCurrent(target)) return ValueTask.FromResult(Failure(TextReadStatus.TargetChanged));
            capture = _router.Capture(target, request, token);
            token.ThrowIfCancellationRequested();
            if (!_isCurrent(target)) return ValueTask.FromResult(Failure(TextReadStatus.TargetChanged));
            var identity = $"{target.Identity}/{capture.Provider}/{capture.ScopeKind}";
            if (_identity != identity) { _identity = identity; _session = Guid.NewGuid().ToString("N"); _last = null; }
            var status = capture.Status;
            TextSnapshot? snapshot = null;
            if (capture.Capture != null)
            {
                var extracted = SentenceExtractor.Extract(capture.Capture, request); status = extracted.Status;
                if (extracted.Mode is { } mode && status is TextReadStatus.Success or TextReadStatus.Composing)
                {
                    var snapshotStatus = status == TextReadStatus.Composing ? "composing" : null;
                    if (_last?.Mode == mode && _last.Text == extracted.Text && _last.Status == snapshotStatus) snapshot = _last;
                    else snapshot = _last = new(_session, ++_version, target.ForegroundWindow, identity, mode, extracted.Text, snapshotStatus);
                }
            }
            if (snapshot == null) _last = null;
            var focus = new InputFocus(_session, target.ForegroundWindow, identity, capture.Capture is { ReadOnly: false });
            result = new(focus, snapshot, Diagnostic(status, identity));
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or TimeoutException)
        {
            var failure = TextReadDiagnostic.FromException(error, resolution?.ForegroundWindow ?? Native.GetForegroundWindow().ToInt64(), "UIA");
            result = Failure(failure.Status) with { Diagnostic = failure };
        }
        return ValueTask.FromResult(result);

        TextReadResult Failure(TextReadStatus status)
        { _identity = null; _last = null; return new(null, null, Diagnostic(status, resolution?.Target?.Identity)); }
        TextReadDiagnostic Diagnostic(TextReadStatus status, string? identity) =>
            new(status, resolution?.ForegroundWindow ?? 0, identity, _session, _version, capture?.Provider, capture?.ScopeKind,
                resolution?.Target?.ProcessName, resolution?.Target?.ClassName, resolution?.Target?.ControlType, capture?.NativeError,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
}
