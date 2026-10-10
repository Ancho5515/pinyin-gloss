using System.Net;
using System.Text;
using PinyinGloss.Core;
using PinyinGloss.Providers;

namespace PinyinGloss.Tests;
public class ProviderTests
{
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
