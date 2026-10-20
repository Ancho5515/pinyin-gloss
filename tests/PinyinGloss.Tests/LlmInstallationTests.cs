using System.Text.Json;
using PinyinGloss.Core;

namespace PinyinGloss.Tests;
public sealed class LlmInstallationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pinyin 模型 " + Guid.NewGuid().ToString("N"));
    public LlmInstallationTests()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "LLM"));
        File.WriteAllText(Path.Combine(_directory, "LLM", "model.gguf"), "fixture");
        File.WriteAllText(Path.Combine(_directory, "LLM", "llama-server.exe"), "fixture");
    }
    private void Write(string model, string quantization = "Q8_0") => File.WriteAllText(Path.Combine(_directory, "llm-installation.json"), JsonSerializer.Serialize(new {
        SchemaVersion = 1, Model = "NiuTrans/LMT-60-1.7B", Quantization = quantization, ModelRepository = "mradermacher/LMT-60-1.7B-GGUF",
        ModelRevision = new string('a', 40), ModelSha256 = new string('b', 64), ModelPath = model,
        ExecutablePath = "LLM/llama-server.exe", Backend = "Cpu", RuntimeTag = "b11077", RuntimeRevision = "982a3329af8401772087d67c54d5948a5b270943"
    }));
    [Fact]
    public void Default_profile_uses_q4_with_gpu_preference()
    {
        var model = AppSettings.CreateDefault().Providers[0];
        Assert.Equal("Q4_K_M", model.Quantization); Assert.Equal("LMT Q4_K_M", model.Name); Assert.True(model.UseGpu);
    }
    [Fact]
    public void Default_capture_covers_all_ten_supported_visible_candidates()
    { Assert.Equal(10, AppSettings.CreateDefault().Capture.MaxCandidates); }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Default_and_custom_paths_resolve_and_new_profile_enables_preload(bool absolute)
    {
        Write(absolute ? Path.Combine(_directory, "LLM", "model.gguf") : "LLM/model.gguf");
        var settings = AppSettings.CreateDefault();
        Assert.True(LlmInstallation.TryApply(settings, _directory, true, out var error)); Assert.Null(error);
        var model = settings.Providers[0]; Assert.True(model.Enabled); Assert.False(model.UseGpu);
        Assert.Equal(Path.Combine(_directory, "LLM", "model.gguf"), model.ModelPath);
        Assert.True(settings.Startup.PreloadModel); SettingsValidator.ThrowIfInvalid(settings);
    }
    [Fact]
    public void Auto_manifest_enables_gpu_and_resolves_cpu_backup()
    {
        Write("LLM/model.gguf");
        var path = Path.Combine(_directory, "llm-installation.json");
        var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        manifest["Backend"] = "Auto"; manifest["CpuExecutablePath"] = "LLM/llama-server.exe";
        File.WriteAllText(path, manifest.ToJsonString());
        var settings = AppSettings.CreateDefault();
        Assert.True(LlmInstallation.TryApply(settings, _directory, true, out var error), error);
        Assert.True(settings.Providers[0].UseGpu); SettingsValidator.ThrowIfInvalid(settings);
    }
    [Fact]
    public void Existing_disabled_profile_and_manual_paths_are_preserved()
    {
        Write("LLM/model.gguf"); var settings = AppSettings.CreateDefault();
        Assert.True(LlmInstallation.TryApply(settings, _directory, false, out _));
        Assert.False(settings.Providers[0].Enabled); Assert.False(settings.Startup.PreloadModel);
        settings.Providers[0].ModelPath = "manual.gguf";
        settings.Providers[0].UseInstalledModel = false;
        Assert.False(LlmInstallation.TryApply(settings, _directory, true, out _));
        Assert.Equal("manual.gguf", settings.Providers[0].ModelPath);
    }
    [Fact]
    public void Saved_automatic_installation_refreshes_paths_without_enabling_disabled_model()
    {
        Write("LLM/model.gguf"); var settings = AppSettings.CreateDefault();
        Assert.True(LlmInstallation.TryApply(settings, _directory, true, out _));
        settings = SettingsJson.Clone(settings);
        settings.Providers[0].ModelPath = "old-location.gguf"; settings.Providers[0].Enabled = false;
        Assert.True(LlmInstallation.TryApply(settings, _directory, false, out _));
        Assert.Equal(Path.Combine(_directory, "LLM", "model.gguf"), settings.Providers[0].ModelPath);
        Assert.False(settings.Providers[0].Enabled);
    }
    [Theory]
    [InlineData("Q6_K")]
    [InlineData("Q5_K_M")]
    [InlineData("Q4_K_M")]
    public void Explicit_supported_quantization_is_applied(string quantization)
    {
        Write("LLM/model.gguf", quantization); var settings = AppSettings.CreateDefault();
        Assert.True(LlmInstallation.TryApply(settings, _directory, true, out var error), error);
        Assert.Equal(quantization, settings.Providers[0].Quantization);
    }
    [Theory]
    [InlineData("missing.gguf", "Q8_0")]
    [InlineData("LLM/model.gguf", "Q2_K")]
    public void Incomplete_or_wrong_quantization_does_not_partially_change_settings(string path, string quantization)
    {
        Write(path, quantization); var settings = AppSettings.CreateDefault(); var before = SettingsJson.Clone(settings);
        Assert.False(LlmInstallation.TryApply(settings, _directory, true, out var error)); Assert.NotNull(error);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(settings));
    }
    public void Dispose() => Directory.Delete(_directory, true);
}
