namespace PinyinGloss.Core;

public enum ProviderKind { LocalDictionary, LocalModel, OnlineModel }
public enum ResultStatus { Ready, NotFound, Timeout, Cancelled, InvalidOutput, Unavailable, Skipped }
public record ScreenRect(double X, double Y, double Width, double Height);
public record Candidate(string Id, string Text, int VisibleIndex);
public record CandidateSnapshot(string SessionId, string SnapshotId, long Generation, string WindowIdentity,
    ScreenRect Bounds, IReadOnlyList<Candidate> Candidates, string? SelectedCandidateId = null, string CaptureMethod = "UIA");
public record ProviderDescriptor(string Id, string Version, ProviderKind Kind, IReadOnlyList<string> SupportedTargets,
    bool SupportsPrompt, bool SupportsHardCancel);
public record ProviderQuery(Candidate Candidate, string TargetLanguage, string Prompt, int RemainingBudgetMs);
public record ProviderResult(string CandidateId, string OriginalText, string TargetLanguage, ResultStatus Status,
    string? Gloss = null, string? ErrorCode = null);
public record TranslationCell(string SnapshotId, long Generation, long SettingsRevision, string ColumnId,
    string TargetLanguage, string ActualSourceId, string SourceVersion, ProviderResult Result);
public interface ITranslationProvider
{
    ProviderDescriptor Describe();
    Task StartAsync(CancellationToken token);
    Task StopAsync(CancellationToken token);
    Task<ProviderResult> QueryAsync(ProviderQuery query, CancellationToken token);
}
public enum ReaderMessageKind { Snapshot, Hidden, Heartbeat, Status }
public record ReaderMessage(ReaderMessageKind Kind, CandidateSnapshot? Snapshot = null, string? Status = null, int ProtocolVersion = 1);
