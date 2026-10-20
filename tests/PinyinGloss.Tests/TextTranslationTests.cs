using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PinyinGloss.Core;
using PinyinGloss.Providers;

namespace PinyinGloss.Tests;

public class TextTranslationTests
{
    [Fact]
    public async Task Model_load_completion_retries_only_the_current_loading_text_once()
    {
        var settings = AppSettings.CreateDefault(); settings.TextTranslation.SentenceEnabled = true; settings.TextTranslation.StableMs = 0;
        var snapshot = new TextSnapshot("focus", 1, 10, "edit", TextMode.Sentence, "我喜欢苹果。");
        var calls = 0; var results = new List<TextTranslation>();
        using var coordinator = new TextTranslationCoordinator(settings, (text, _) => Task.FromResult(new ProviderResult($"{text.SessionId}/{text.Version}", text.Text, "en",
            ++calls == 1 ? ResultStatus.Unavailable : ResultStatus.Ready, calls == 1 ? null : "I like apples.", calls == 1 ? "model_loading" : null)), "v1");
        coordinator.ResultUpdated += r => results.Add(r);
        coordinator.Submit(snapshot);
        var retry = typeof(TextTranslationCoordinator).GetMethod("RetryAfterModelLoad"); Assert.NotNull(retry);
        retry.Invoke(coordinator, null); retry.Invoke(coordinator, null);
        Assert.Equal(2, calls); Assert.Equal(ResultStatus.Ready, results[^1].Result.Status);
        coordinator.Invalidate(); retry.Invoke(coordinator, null); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Multiline_selection_keeps_original_but_uses_one_prompt_input_line()
    {
        var provider = new CountingProvider(); var settings = AppSettings.CreateDefault();
        settings.Providers.Single(p => p.Id == "lmt").Enabled = true; settings.TextTranslation.SelectionEnabled = true;
        using var coordinator = new TranslationCoordinator(new Dictionary<string, ITranslationProvider> { ["lmt"] = provider }, settings);
        const string original = "这是第一句。\r\n这是第二句。";
        var result = await coordinator.QueryTextAsync(new("focus", 1, 10, "edit", TextMode.Selection, original), default);
        Assert.Equal(original, result.OriginalText); Assert.Equal(original, Assert.Single(provider.Queries).Candidate.Text);
        Assert.Contains("Chinese: 这是第一句。 这是第二句。\nEnglish:", provider.Queries[0].Prompt);
    }

    [Theory]
    [InlineData(368, ResultStatus.Ready)]
    [InlineData(369, ResultStatus.InvalidOutput)]
    public async Task Text_output_reservation_reduces_input_budget_without_changing_candidate_defaults(int inputTokens, ResultStatus expected)
    {
        var handler = new TextBudgetHandler(inputTokens);
        var config = new ProviderSettings { Id = "model", Kind = ProviderKind.LocalModel, ContextTokens = 512, InputTokenBudget = 448, MaxOutputTokens = 32 };
        using var provider = new HttpModelProvider(config, new(), new HttpClient(handler));
        var result = await provider.QueryAsync(new(new("focus/1", "我喜欢苹果。", 1), "en", "Translate", 1000, 128, false), default);
        Assert.Equal(expected, result.Status); Assert.Equal(32, config.MaxOutputTokens);
        if (expected == ResultStatus.Ready)
        { Assert.NotNull(handler.Completion); Assert.Equal(128, handler.Completion["n_predict"]!.GetValue<int>()); Assert.False(handler.Completion["cache_prompt"]!.GetValue<bool>()); }
        else { Assert.Null(handler.Completion); Assert.Equal("prompt_token_budget", result.ErrorCode); }
    }

    private sealed class TextBudgetHandler(int inputTokens) : HttpMessageHandler
    {
        public JsonNode? Completion;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/completion") Completion = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token));
            return new(HttpStatusCode.OK) { Content = new StringContent(path switch
            {
                "/apply-template" => "{\"prompt\":\"Translate\"}",
                "/tokenize" => JsonSerializer.Serialize(new { tokens = Enumerable.Repeat(1, inputTokens).ToArray() }),
                _ => "data: {\"content\":\"I like apples.\",\"stop\":true,\"stop_type\":\"eos\"}\n\n"
            }, Encoding.UTF8) };
        }
    }

    [Fact]
    public async Task Latest_text_gets_next_slot_before_a_waiting_candidate_page()
    {
        var provider = new PriorityProvider(); var settings = AppSettings.CreateDefault();
        settings.Providers.Single(p => p.Id == "lmt").Enabled = true; settings.TextTranslation.SentenceEnabled = true;
        settings.Response.StableMs = 0; settings.Response.SnapshotDeadlineMs = 5000;
        settings.Columns = [new() { Id = "en", SourceId = "lmt", TargetLanguage = "en" }];
        using var coordinator = new TranslationCoordinator(new Dictionary<string, ITranslationProvider> { ["lmt"] = provider }, settings);
        coordinator.Submit(CoreTests.Snapshot("old", "旧候选"));
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        coordinator.Submit(CoreTests.Snapshot("new", "新候选"));
        await Task.Delay(80);
        var text = coordinator.QueryTextAsync(new("focus", 1, 10, "edit", TextMode.Sentence, "当前句子"), default);
        provider.Release.TrySetResult(); await text; await coordinator.WhenIdleAsync();
        Assert.Equal(new[] { "旧候选", "当前句子", "新候选" }, provider.Order);
    }

    private sealed class PriorityProvider : ITranslationProvider
    {
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Order = [];
        public ProviderDescriptor Describe() => new("lmt", "v1", ProviderKind.LocalModel, ["en"], true, false);
        public Task StartAsync(CancellationToken t) => Task.CompletedTask;
        public Task StopAsync(CancellationToken t) => Task.CompletedTask;
        public async Task<ProviderResult> QueryAsync(ProviderQuery q, CancellationToken t)
        {
            Order.Add(q.Candidate.Text);
            if (Order.Count == 1) { Started.TrySetResult(); await Release.Task; }
            return new(q.Candidate.Id, q.Candidate.Text, q.TargetLanguage, ResultStatus.Ready, "translated");
        }
    }
    [Fact]
    public async Task Paused_application_does_not_send_text_to_model()
    {
        var provider = new CountingProvider(); var settings = AppSettings.CreateDefault();
        settings.Enabled = false; settings.TextTranslation.SentenceEnabled = true;
        settings.Providers.Single(p => p.Id == "lmt").Enabled = true;
        using var coordinator = new TranslationCoordinator(new Dictionary<string, ITranslationProvider> { ["lmt"] = provider }, settings);
        var result = await coordinator.QueryTextAsync(new("focus", 1, 10, "edit", TextMode.Sentence, "我喜欢苹果。"), default);
        Assert.Equal(ResultStatus.Unavailable, result.Status); Assert.Empty(provider.Queries);
    }
    [Fact]
    public async Task Text_queries_use_candidate_slot_without_entering_memory_cache()
    {
        var provider = new CountingProvider(); var settings = AppSettings.CreateDefault();
        settings.Providers.Single(p => p.Id == "lmt").Enabled = true;
        settings.TextTranslation.SentenceEnabled = true;
        using var coordinator = new TranslationCoordinator(new Dictionary<string, ITranslationProvider> { ["lmt"] = provider }, settings);
        var method = typeof(TranslationCoordinator).GetMethod("QueryTextAsync"); Assert.NotNull(method);
        var snapshot = new TextSnapshot("focus", 1, 10, "edit", TextMode.Sentence, "我喜欢苹果。");
        async Task<ProviderResult> Query() => await (Task<ProviderResult>)method.Invoke(coordinator, [snapshot, CancellationToken.None])!;
        var results = await Task.WhenAll(Query(), Query());
        Assert.All(results, r => Assert.Equal(ResultStatus.Ready, r.Status));
        Assert.Equal(2, provider.Queries.Count); Assert.Equal(1, provider.MaxBusy);
        Assert.All(provider.Queries, q => { Assert.Equal(128, q.MaxOutputTokens); Assert.False(q.CachePrompt); });
    }

    [Fact]
    public async Task Text_scheduler_rejects_late_result_after_focus_is_cleared()
    {
        var type = typeof(AppSettings).Assembly.GetType("PinyinGloss.Core.TextTranslationCoordinator"); Assert.NotNull(type);
        var settings = AppSettings.CreateDefault(); settings.TextTranslation.SentenceEnabled = true;
        settings.TextTranslation.StableMs = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<ProviderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<TextSnapshot, CancellationToken, Task<ProviderResult>> query = (s, _) => { started.TrySetResult(); return release.Task; };
        using var scheduler = (IDisposable)Activator.CreateInstance(type, settings, query, "v1")!;
        var results = new List<TextTranslation>();
        type.GetEvent("ResultUpdated")!.AddEventHandler(scheduler, (Action<TextTranslation>)(r => results.Add(r)));
        var text = new TextSnapshot("focus", 1, 10, "edit", TextMode.Sentence, "我喜欢苹果。");
        type.GetMethod("Submit")!.Invoke(scheduler, [text]);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        type.GetMethod("Invalidate")!.Invoke(scheduler, null);
        release.SetResult(new("text", text.Text, "en", ResultStatus.Ready, "I like apples."));
        await Task.Delay(80);
        Assert.DoesNotContain(results, r => r.Result.Status == ResultStatus.Ready);
    }

    private sealed class CountingProvider : ITranslationProvider
    {
        public List<ProviderQuery> Queries { get; } = [];
        private int _busy; public int MaxBusy;
        public ProviderDescriptor Describe() => new("lmt", "v1", ProviderKind.LocalModel, ["en"], true, true);
        public Task StartAsync(CancellationToken t) => Task.CompletedTask;
        public Task StopAsync(CancellationToken t) => Task.CompletedTask;
        public async Task<ProviderResult> QueryAsync(ProviderQuery q, CancellationToken t)
        {
            var busy = Interlocked.Increment(ref _busy); MaxBusy = Math.Max(MaxBusy, busy); Queries.Add(q);
            try { await Task.Delay(20, t); return new(q.Candidate.Id, q.Candidate.Text, q.TargetLanguage, ResultStatus.Ready, "translated"); }
            finally { Interlocked.Decrement(ref _busy); }
        }
    }
    [Fact]
    public void Settings_accept_independent_text_switches_and_preserve_old_profiles()
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(AppSettings.CreateDefault(), SettingsJson.Options))!;
        json["textTranslation"] = new JsonObject { ["sentenceEnabled"] = true, ["selectionEnabled"] = false, ["sourceId"] = "lmt" };
        var settings = JsonSerializer.Deserialize<AppSettings>(json.ToJsonString(), SettingsJson.Options)!;
        Assert.Empty(SettingsValidator.Validate(settings));
        Assert.Contains("\"selectionEnabled\": false", JsonSerializer.Serialize(settings, SettingsJson.Options));
    }

    [Fact]
    public async Task Output_ending_at_token_limit_is_rejected_as_truncated()
    {
        using var provider = new HttpModelProvider(new() { Id = "model", Kind = ProviderKind.LocalModel }, new(), new HttpClient(new LimitHandler()));
        var result = await provider.QueryAsync(new(new("sentence", "我喜欢苹果。", 1), "en", "Translate", 1000), default);
        Assert.Equal(ResultStatus.InvalidOutput, result.Status);
        Assert.Equal("output_truncated", result.ErrorCode);
    }

    private sealed class LimitHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(r.RequestUri!.AbsolutePath switch
            {
                "/apply-template" => "{\"prompt\":\"Translate\"}",
                "/tokenize" => "{\"tokens\":[1,2]}",
                _ => "data: {\"content\":\"I like\",\"stop\":true,\"stop_type\":\"limit\"}\n\n"
            }, Encoding.UTF8)
        });
    }
}
