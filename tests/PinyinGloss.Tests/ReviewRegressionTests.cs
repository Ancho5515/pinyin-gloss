using System.Text.Json;
using PinyinGloss.Core;
namespace PinyinGloss.Tests;
public class ReviewRegressionTests
{
    [Fact]
    public async Task Deadline_keeps_a_completed_model_translation()
    {
        var settings = CoreTests.ModelSettings(); settings.Response.SnapshotDeadlineMs = 150;
        using var c = new TranslationCoordinator(new Dictionary<string,ITranslationProvider> { ["model"] = new PartialProvider() }, settings);
        var states = new Dictionary<string,ResultStatus>(); c.CellUpdated += cell => states[cell.Result.OriginalText] = cell.Result.Status;
        c.Submit(CoreTests.Snapshot("deadline", "苹果", "保险")); await c.WhenIdleAsync();
        Assert.Equal(ResultStatus.Ready, states["苹果"]); Assert.Equal(ResultStatus.Timeout, states["保险"]);
    }
    [Fact]
    public void Online_source_omitting_enablement_defaults_disabled()
    {
        var config = JsonSerializer.Deserialize<ProviderSettings>("{\"id\":\"online\",\"kind\":\"OnlineModel\",\"endpoint\":\"https://example.test/\"}", SettingsJson.Options)!;
        Assert.False(config.Enabled);
    }
    [Fact]
    public async Task Clean_reader_eof_invalidates_the_snapshot()
    {
        using var stream = new MemoryStream();
        await PipeProtocol.WriteAsync(stream, new ReaderMessage(ReaderMessageKind.Snapshot, CoreTests.Snapshot("reader", "苹果")), default);
        stream.Position = 0; var messages = new List<ReaderMessage>();
        await PipeProtocol.PumpReaderAsync(stream, messages.Add, default);
        Assert.Equal(ReaderMessageKind.Hidden, messages.Last().Kind);
    }
    [Fact]
    public async Task Manual_query_waits_for_actual_cancel_cleanup_and_shares_the_slot()
    {
        var p = new CountingControlledProvider(); var settings = CoreTests.ModelSettings();
        using var c = new TranslationCoordinator(new Dictionary<string,ITranslationProvider> { ["model"] = p }, settings);
        c.Submit(CoreTests.Snapshot("pending", "苹果")); await p.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        c.Hide(); await c.WhenIdleAsync();
        var manual = c.QueryManualAsync(settings.Columns[0], "保险", default);
        try { Assert.Equal(1, p.QueryCount); }
        finally { p.Release.TrySetResult(); }
        Assert.Equal(ResultStatus.Ready, (await manual.WaitAsync(TimeSpan.FromSeconds(3))).Status);
        Assert.Equal(2, p.QueryCount);
    }
    [Fact]
    public async Task Repeated_manual_queries_are_serialized()
    {
        var p = new CountingControlledProvider(); var settings = CoreTests.ModelSettings();
        using var c = new TranslationCoordinator(new Dictionary<string,ITranslationProvider> { ["model"] = p }, settings);
        var first = c.QueryManualAsync(settings.Columns[0], "苹果", default); await p.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = c.QueryManualAsync(settings.Columns[0], "保险", default);
        try { Assert.Equal(1, p.QueryCount); } finally { p.Release.TrySetResult(); }
        await Task.WhenAll(first, second); Assert.Equal(2, p.QueryCount);
    }
}
internal sealed class PartialProvider : RecordingProvider
{
    public override async Task<ProviderResult> QueryAsync(ProviderQuery q, CancellationToken token)
    { if (q.Candidate.Text == "保险") await Task.Delay(Timeout.Infinite, token); return await base.QueryAsync(q, token); }
}
internal sealed class CountingControlledProvider : RecordingProvider
{
    public int QueryCount; public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async Task<ProviderResult> QueryAsync(ProviderQuery q, CancellationToken token)
    { Interlocked.Increment(ref QueryCount); Started.TrySetResult(); await Release.Task; return await base.QueryAsync(q, default); }
}
