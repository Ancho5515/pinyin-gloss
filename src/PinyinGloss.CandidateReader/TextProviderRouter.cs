using PinyinGloss.Core;

namespace PinyinGloss.CandidateReader;

internal sealed record ProviderCaptureResult(TextReadStatus Status, CapturedText? Capture, string Provider,
    string? ScopeKind = null, int? NativeError = null);
internal interface ITextProvider
{
    string Id { get; }
    ProviderCaptureResult Capture(ResolvedTextTarget target, TextReadRequest request, CancellationToken token);
}
internal sealed class TextProviderRouter(params ITextProvider[] providers)
{
    internal static bool CanFallback(TextReadStatus status) => status is TextReadStatus.NoSuitableProvider or TextReadStatus.UnsupportedControl;
    internal ProviderCaptureResult Capture(ResolvedTextTarget target, TextReadRequest request, CancellationToken token)
    {
        if (target.Password) return new(TextReadStatus.PasswordTarget, null, "None");
        ProviderCaptureResult result = new(TextReadStatus.NoSuitableProvider, null, "None");
        foreach (var provider in providers)
        {
            token.ThrowIfCancellationRequested();
            try { result = provider.Capture(target, request, token); }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or TimeoutException)
            {
                var failure = TextReadDiagnostic.FromException(error, target.ForegroundWindow, provider.Id);
                result = new(failure.Status, null, provider.Id, NativeError: failure.ErrorCode);
            }
            if (result.Status == TextReadStatus.Success && result.Capture == null)
                result = result with { Status = provider.Id == "UIA" ? TextReadStatus.UiaError : provider.Id == "IA2" ? TextReadStatus.Ia2Error : TextReadStatus.NativeProviderError };
            if (!CanFallback(result.Status)) break;
        }
        return result;
    }
}
