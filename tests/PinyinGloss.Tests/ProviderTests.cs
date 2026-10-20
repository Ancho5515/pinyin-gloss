using System.Net;
using System.Text;
using PinyinGloss.Core;
using PinyinGloss.Providers;

namespace PinyinGloss.Tests;
public class ProviderTests
{
    [Fact]
    public async Task Dictionary_primary_uses_same_language_fallback_without_replacing_primary_hits()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var primaryTsv = Path.Combine(directory, "primary.tsv"); var fallbackTsv = Path.Combine(directory, "fallback.tsv");
            var primaryDb = Path.Combine(directory, "primary.db"); var fallbackDb = Path.Combine(directory, "fallback.db");
            await File.WriteAllTextAsync(primaryTsv, "苹果\ten\tprimary apple\n");
            await File.WriteAllTextAsync(fallbackTsv, "苹果\ten\tfallback apple\n保险\ten\tinsurance\n");
            await DictionaryStore.ImportAsync(primaryTsv, primaryDb, "Primary", "MIT", "authored test data", default);
            await DictionaryStore.ImportAsync(fallbackTsv, fallbackDb, "Fallback", "MIT", "authored test data", default);
            var settings = AppSettings.CreateDefault(); settings.Response.StableMs = 0;
            settings.Providers = [new() { Id = "primary", Kind = ProviderKind.LocalDictionary, DictionaryPath = primaryDb },
                new() { Id = "fallback", Kind = ProviderKind.LocalDictionary, DictionaryPath = fallbackDb }];
            settings.Columns = [new() { Id = "en", SourceId = "primary", TargetLanguage = "en", FallbackSourceIds = ["fallback"] }];
            SettingsValidator.ThrowIfInvalid(settings);
            using var coordinator = new TranslationCoordinator(ProviderFactory.Create(settings), settings);
            var cells = new Dictionary<string, TranslationCell>();
            coordinator.CellUpdated += c => cells[c.Result.OriginalText] = c;
            coordinator.Submit(CoreTests.Snapshot("dictionary-chain", "苹果", "保险", "未收录测试词"));
            await coordinator.WhenIdleAsync();
            Assert.Equal("primary apple", cells["苹果"].Result.Gloss);
            Assert.Equal("primary", cells["苹果"].ActualSourceId);
            Assert.Equal(ResultStatus.Ready, cells["保险"].Result.Status);
            Assert.Equal("insurance", cells["保险"].Result.Gloss);
            Assert.Equal("fallback", cells["保险"].ActualSourceId);
            Assert.Equal(ResultStatus.NotFound, cells["未收录测试词"].Result.Status);
            Assert.Null(cells["未收录测试词"].Result.Gloss);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Real_dictionary_fallback_keeps_parallel_sources_and_never_fills_japanese_with_english()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var tsv = Path.Combine(directory, "test.tsv"); var db = Path.Combine(directory, "test.db");
            await File.WriteAllTextAsync(tsv, "保险\ten\tinsurance\n保鲜\ten\tkeep fresh\n包线\ten\twrapped wire\n");
            await DictionaryStore.ImportAsync(tsv, db, "Test", "MIT", "authored test data", default);
            var settings = AppSettings.CreateDefault(); settings.Response.StableMs = 0;
            settings.Providers.Single(p => p.Id == "dictionary").DictionaryPath = db;
            settings.Columns.Add(new() { Id = "lmt-ja", SourceId = "lmt", TargetLanguage = "ja" });
            SettingsValidator.ThrowIfInvalid(settings);
            var providers = ProviderFactory.Create(settings);
            try
            {
                using var coordinator = new TranslationCoordinator(providers, settings);
                var cells = new Dictionary<(string, string), TranslationCell>();
                coordinator.CellUpdated += c => cells[(c.Result.OriginalText, c.ColumnId)] = c;
                coordinator.Submit(CoreTests.Snapshot("parallel", "保险", "保鲜", "包线"));
                await coordinator.WhenIdleAsync();
                foreach (var (word, gloss) in new[] { ("保险", "insurance"), ("保鲜", "keep fresh"), ("包线", "wrapped wire") })
                {
                    foreach (var column in new[] { "lmt-en", "dictionary-en" })
                    {
                        Assert.Equal(ResultStatus.Ready, cells[(word, column)].Result.Status);
                        Assert.Equal(gloss, cells[(word, column)].Result.Gloss);
                        Assert.Equal("dictionary", cells[(word, column)].ActualSourceId);
                    }
                    Assert.Equal(ResultStatus.Unavailable, cells[(word, "lmt-ja")].Result.Status);
                    Assert.Null(cells[(word, "lmt-ja")].Result.Gloss);
                }
            }
            finally { foreach (var provider in providers.Values.OfType<IDisposable>()) provider.Dispose(); }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Dictionary_import_is_atomic_and_never_crosses_language()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var tsv = Path.Combine(directory, "test.tsv"); var db = Path.Combine(directory, "test.db");
            await File.WriteAllTextAsync(tsv, "苹果\ten\tapple\n保险\tja\t保険\n");
            await DictionaryStore.ImportAsync(tsv, db, "Test", "MIT", "authored test data", default);
            Assert.Equal("apple", await DictionaryStore.LookupAsync(db, "苹果", "en", default));
            Assert.Null(await DictionaryStore.LookupAsync(db, "苹果", "ja", default));
            await File.WriteAllTextAsync(tsv, "malformed row\n");
            await Assert.ThrowsAsync<InvalidDataException>(() => DictionaryStore.ImportAsync(tsv, db, "Test", "MIT", "test", default));
            Assert.Equal("apple", await DictionaryStore.LookupAsync(db, "苹果", "en", default));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Theory]
    [InlineData("<think>reasoning</think>apple")]
    [InlineData("```json\n{}\n```")]
    [InlineData("")]
    public void Invalid_model_output_is_rejected(string output) => Assert.False(OutputValidator.IsValid(output));

    [Fact]
    public async Task Llama_counts_chat_template_tokens_before_generation()
    {
        var handler = new FakeHandler();
        var config = new ProviderSettings { Id = "local", Kind = ProviderKind.LocalModel, InputTokenBudget = 10 };
        using var provider = new HttpModelProvider(config, new ResponseSettings(), new HttpClient(handler));
        var result = await provider.QueryAsync(new(new("1", "苹果", 1), "en", "Translate 苹果", 1000), default);
        Assert.Equal(ResultStatus.InvalidOutput, result.Status);
        Assert.Equal(new[] { "/apply-template", "/tokenize" }, handler.Paths);
    }
    [Fact]
    public async Task Disabled_online_source_never_sends_a_request()
    {
        var h = new FakeHandler();
        using var p = new HttpModelProvider(new() { Id = "online", Kind = ProviderKind.OnlineModel, Enabled = false, LlamaApi = false, Endpoint = "https://example.test/" }, new(), new HttpClient(h));
        var result = await p.QueryAsync(new(new("1", "苹果", 1), "en", "Translate", 1000), default);
        Assert.Equal(ResultStatus.Unavailable, result.Status); Assert.Empty(h.Paths);
    }
    [Fact]
    public async Task Streaming_completion_is_collected_as_short_gloss()
    {
        var h = new FakeHandler(2);
        using var p = new HttpModelProvider(new() { Id = "local", Kind = ProviderKind.LocalModel }, new(), new HttpClient(h));
        var result = await p.QueryAsync(new(new("1", "苹果", 1), "en", "Translate", 1000), default);
        Assert.Equal("apple", result.Gloss); Assert.Equal(ResultStatus.Ready, result.Status);
    }
}
internal sealed class FakeHandler(int tokens = 100) : HttpMessageHandler
{
    public List<string> Paths { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var path = request.RequestUri!.AbsolutePath; Paths.Add(path);
        var response = path switch {
            "/apply-template" => "{\"prompt\":\"FULL CHAT TEMPLATE\"}",
            "/tokenize" => "{\"tokens\":[" + string.Join(',', Enumerable.Repeat(1, tokens)) + "]}",
            "/completion" => "data: {\"content\":\"apple\",\"stop\":false}\n\ndata: {\"content\":\"\",\"stop\":true}\n\n",
            _ => "{}"
        };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8) });
    }
}
