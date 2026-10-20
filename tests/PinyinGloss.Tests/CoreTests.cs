using PinyinGloss.Core;

namespace PinyinGloss.Tests;

public class CoreTests
{
    [Fact]
    public void Prompt_replaces_only_allowed_variables_and_preserves_candidate_literals()
    {
        Assert.Equal("Translate {{target_language_name}} into English", PromptTemplate.Render(
            "Translate {{candidate}} into {{target_language_name}}", "{{target_language_name}}", "en"));
        Assert.Throws<ArgumentException>(() => PromptTemplate.Render("{{file}}", "苹果", "en"));
    }

    [Fact]
    public void Settings_reject_wrong_language_fallback_and_duplicate_columns()
    {
        var s = AppSettings.CreateDefault();
        s.Columns[0].TargetLanguage = "ja";
        Assert.Contains(SettingsValidator.Validate(s), x => x.Contains("fallback", StringComparison.OrdinalIgnoreCase));
        s = AppSettings.CreateDefault();
        s.Columns.Add(s.Columns[0] with { });
        Assert.NotEmpty(SettingsValidator.Validate(s));
    }

    [Fact]
    public async Task Protocol_rejects_oversized_frames_before_allocating()
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(100_000_000));
        await Assert.ThrowsAsync<InvalidDataException>(() => PipeProtocol.ReadAsync<ReaderMessage>(stream, default));
    }

    [Fact]
    public async Task New_snapshot_and_hide_reject_provider_that_ignores_cancellation()
    {
        var late = new ControlledProvider();
        var s = ModelSettings();
        using var c = new TranslationCoordinator(new Dictionary<string, ITranslationProvider> { ["model"] = late }, s);
        var cells = new List<TranslationCell>();
        c.CellUpdated += cell => { lock (cells) cells.Add(cell); };
        c.Submit(Snapshot("old", "苹果"));
        await late.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        c.Submit(Snapshot("new", "保险"));
        c.Hide();
        late.Release.TrySetResult();
        await c.WhenIdleAsync();
        Assert.DoesNotContain(cells, x => x.Result.Status == ResultStatus.Ready);
    }

    [Fact]
    public async Task Selected_candidate_rotates_languages_before_next_candidate()
    {
        var provider = new RecordingProvider();
        var s = ModelSettings();
        s.Columns.Add(s.Columns[0] with { Id = "ja", TargetLanguage = "ja" });
        using var c = new TranslationCoordinator(new Dictionary<string, ITranslationProvider> { ["model"] = provider }, s);
        c.Submit(Snapshot("one", "苹果", "保险") with { SelectedCandidateId = "1" });
        await c.WhenIdleAsync();
        Assert.Equal(new[] { "保险:en", "保险:ja", "苹果:en", "苹果:ja" }, provider.Calls);
    }

    [Fact]
    public async Task Failure_uses_same_language_dictionary_and_reports_actual_source()
    {
        var s = ModelSettings();
        s.Providers.Add(new ProviderSettings { Id = "dict", Kind = ProviderKind.LocalDictionary, SupportedTargets = ["en"] });
        s.Columns[0].FallbackSourceIds = ["dict"];
        var providers = new Dictionary<string, ITranslationProvider> {
            ["model"] = new RecordingProvider(ResultStatus.Unavailable),
            ["dict"] = new RecordingProvider(ResultStatus.Ready, ProviderKind.LocalDictionary)
        };
        using var c = new TranslationCoordinator(providers, s);
        TranslationCell? ready = null;
        c.CellUpdated += x => { if (x.Result.Status == ResultStatus.Ready) ready = x; };
        c.Submit(Snapshot("one", "苹果"));
        await c.WhenIdleAsync();
        Assert.NotNull(ready);
        Assert.Equal("dict", ready.ActualSourceId);
        Assert.Equal("en", ready.TargetLanguage);
    }

    [Fact]
    public async Task Wrong_language_response_is_never_published_as_translation()
    {
        var provider = new WrongLanguageProvider();
        using var c = new TranslationCoordinator(new Dictionary<string, ITranslationProvider> { ["model"] = provider }, ModelSettings());
        var statuses = new List<ResultStatus>();
        c.CellUpdated += x => statuses.Add(x.Result.Status);
        c.Submit(Snapshot("one", "苹果"));
        await c.WhenIdleAsync();
        Assert.DoesNotContain(ResultStatus.Ready, statuses);
    }

    [Fact]
    public async Task Deadline_includes_debounce_and_queue()
    {
        var p = new RecordingProvider();
        var s = ModelSettings(); s.Response.StableMs = 80; s.Response.SnapshotDeadlineMs = 40;
        using var c = new TranslationCoordinator(new Dictionary<string, ITranslationProvider> { ["model"] = p }, s);
        c.Submit(Snapshot("one", "苹果"));
        await c.WhenIdleAsync();
        Assert.Empty(p.Calls);
    }

    [Theory]
    [InlineData(ResultStatus.Ready)]
    [InlineData(ResultStatus.NotFound)]
    public async Task Dictionary_results_do_not_wait_for_model_stability(ResultStatus status)
    {
        var settings = AppSettings.CreateDefault();
        settings.Response.StableMs = 2000;
        settings.Response.SnapshotDeadlineMs = 3000;
        settings.Providers = [new() { Id = "dictionary", Kind = ProviderKind.LocalDictionary }];
        settings.Columns = [new() { Id = "en", SourceId = "dictionary", TargetLanguage = "en" }];
        var provider = new RecordingProvider(status, ProviderKind.LocalDictionary);
        using var coordinator = new TranslationCoordinator(new Dictionary<string, ITranslationProvider> { ["dictionary"] = provider }, settings);
        var result = new TaskCompletionSource<TranslationCell>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.CellUpdated += cell => result.TrySetResult(cell);
        coordinator.Submit(Snapshot("dictionary-immediate", "缺词测试"));
        var cell = await result.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(status, cell.Result.Status);
        await coordinator.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Updating_settings_invalidates_inflight_result()
    {
        var p = new ControlledProvider(); var s = ModelSettings();
        using var c = new TranslationCoordinator(new Dictionary<string, ITranslationProvider> { ["model"] = p }, s);
        var ready = false; c.CellUpdated += x => ready |= x.Result.Status == ResultStatus.Ready;
        c.Submit(Snapshot("one", "苹果"));
        await p.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        c.UpdateSettings(s with { SettingsRevision = 2 }); p.Release.TrySetResult();
        await c.WhenIdleAsync();
        Assert.False(ready);
    }

    [Fact]
    public async Task Cache_preserves_recently_used_translation_when_capacity_is_exceeded()
    {
        var p = new RecordingProvider(); var s = ModelSettings(); s.Response.MemoryCacheEntries = 2;
        using var c = new TranslationCoordinator(new Dictionary<string, ITranslationProvider> { ["model"] = p }, s);
        foreach (var word in new[] { "苹果", "保险", "苹果", "支持", "苹果" }) { c.Submit(Snapshot(Guid.NewGuid().ToString(), word)); await c.WhenIdleAsync(); }
        Assert.Equal(new[] { "苹果:en", "保险:en", "支持:en" }, p.Calls);
    }

    [Fact]
    public async Task Model_without_dictionary_publishes_waiting_before_stability_delay()
    {
        var settings = ModelSettings(); settings.Response.StableMs = 2000;
        settings.Response.SnapshotDeadlineMs = 3000;
        var provider = new RecordingProvider();
        using var coordinator = new TranslationCoordinator(new Dictionary<string, ITranslationProvider> { ["model"] = provider }, settings);
        var first = new TaskCompletionSource<TranslationCell>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.CellUpdated += cell => first.TrySetResult(cell);
        coordinator.Submit(Snapshot("waiting", "苹果"));
        var cell = await first.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(ResultStatus.Pending, cell.Result.Status);
        Assert.Empty(provider.Calls);
        coordinator.Hide(); await coordinator.WhenIdleAsync();
    }

    [Fact]
    public async Task Waiting_does_not_count_as_finished_at_snapshot_deadline()
    {
        var settings = ModelSettings(); settings.Response.StableMs = 2000;
        settings.Response.SnapshotDeadlineMs = 100;
        using var coordinator = new TranslationCoordinator(new Dictionary<string, ITranslationProvider> { ["model"] = new RecordingProvider() }, settings);
        var statuses = new List<ResultStatus>(); coordinator.CellUpdated += cell => statuses.Add(cell.Result.Status);
        coordinator.Submit(Snapshot("deadline", "苹果")); await coordinator.WhenIdleAsync();
        Assert.Equal(new[] { ResultStatus.Pending, ResultStatus.Timeout }, statuses);
    }

    internal static AppSettings ModelSettings()
    {
        var s = AppSettings.CreateDefault(); s.Response.StableMs = 0;
        s.Providers = [new ProviderSettings { Id = "model", Kind = ProviderKind.LocalModel, SupportedTargets = ["en", "ja"] }];
        s.Columns = [new ColumnSettings { Id = "en", SourceId = "model", TargetLanguage = "en" }]; return s;
    }
    internal static CandidateSnapshot Snapshot(string id, params string[] words) => new("test", id, 1, "window-1", new(10, 10, 100, 20),
        words.Select((w, i) => new Candidate(i.ToString(), w, i + 1)).ToArray());
}

internal class RecordingProvider(ResultStatus status = ResultStatus.Ready, ProviderKind kind = ProviderKind.LocalModel) : ITranslationProvider
{
    public List<string> Calls { get; } = [];
    public ProviderDescriptor Describe() => new("test", "v1", kind, ["en", "ja"], true, false);
    public virtual Task<ProviderResult> QueryAsync(ProviderQuery q, CancellationToken token)
    {
        Calls.Add(q.Candidate.Text + ":" + q.TargetLanguage);
        return Task.FromResult(new ProviderResult(q.Candidate.Id, q.Candidate.Text, q.TargetLanguage, status, status == ResultStatus.Ready ? "gloss" : null));
    }
    public Task StartAsync(CancellationToken token) => Task.CompletedTask;
    public Task StopAsync(CancellationToken token) => Task.CompletedTask;
}
internal sealed class ControlledProvider : RecordingProvider
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async Task<ProviderResult> QueryAsync(ProviderQuery q, CancellationToken token)
    {
        Started.TrySetResult(); await Release.Task;
        return await base.QueryAsync(q, CancellationToken.None);
    }
}
internal sealed class WrongLanguageProvider : RecordingProvider
{
    public override Task<ProviderResult> QueryAsync(ProviderQuery q, CancellationToken token) =>
        Task.FromResult(new ProviderResult(q.Candidate.Id, q.Candidate.Text, "ja", ResultStatus.Ready, "wrong"));
}
