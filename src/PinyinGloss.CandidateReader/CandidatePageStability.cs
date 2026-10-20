namespace PinyinGloss.CandidateReader;

internal sealed class CandidatePageStability
{
    private string? _pending;
    private TimeSpan _since;
    internal bool IsConfirming { get; private set; }
    internal bool Observe(string fingerprint, TimeSpan now)
    {
        if (_pending != fingerprint) { _pending = fingerprint; _since = now; IsConfirming = true; return false; }
        if (now - _since < TimeSpan.FromMilliseconds(50)) return false;
        IsConfirming = false;
        return true;
    }
    internal void Reset() { _pending = null; IsConfirming = false; }
}
