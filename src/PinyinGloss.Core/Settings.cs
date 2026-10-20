using System.Text.Json;
using System.Text.Json.Serialization;

namespace PinyinGloss.Core;

public record AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public long SettingsRevision { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public CaptureSettings Capture { get; set; } = new();
    public ResponseSettings Response { get; set; } = new();
    public DisplaySettings Display { get; set; } = new();
    public TextTranslationSettings TextTranslation { get; set; } = new();
    public ResourceSettings Resources { get; set; } = new();
    public StartupSettings Startup { get; set; } = new();
    public List<ProviderSettings> Providers { get; set; } = [];
    public List<ColumnSettings> Columns { get; set; } = [];
    public static AppSettings CreateDefault() => new()
    {
        Providers = [new() { Id = "lmt", Name = "LMT Q4_K_M", Kind = ProviderKind.LocalModel, Enabled = false,
            Managed = true, SupportedTargets = ["en", "ja", "ko", "fr", "de", "es", "ru", "ar", "pt", "it"] },
            new() { Id = "dictionary", Name = "示例词典", Kind = ProviderKind.LocalDictionary, DictionaryPath = "dictionaries/demo.db", SupportedTargets = ["en"] }],
        Columns = [new() { Id = "lmt-en", SourceId = "lmt", TargetLanguage = "en", FallbackSourceIds = ["dictionary"] },
            new() { Id = "dictionary-en", SourceId = "dictionary", TargetLanguage = "en", ColorLight = "#23834B", ColorDark = "#7ACD91" }]
    };
}
public record CaptureSettings
{
    public int PollMs { get; set; } = 50;
    public int WatchdogMs { get; set; } = 500;
    public int MaxCandidates { get; set; } = 10;
}
public record ResponseSettings
{
    public int StableMs { get; set; } = 120;
    public int SnapshotDeadlineMs { get; set; } = 1500;
    public int LocalStallMs { get; set; } = 800;
    public int OnlineStallMs { get; set; } = 2000;
    public int DictionaryTimeoutMs { get; set; } = 50;
    public int CancelGraceMs { get; set; } = 1500;
    public int MemoryCacheEntries { get; set; } = 1000;
}
public record DisplaySettings
{
    public double MaxWidthRatio { get; set; } = 0.8;
    public int OffsetX { get; set; }
    public int OffsetY { get; set; } = 8;
    public bool Dark { get; set; }
    public bool FollowSystemTheme { get; set; } = true;
}
public record TextTranslationSettings
{
    public bool SentenceEnabled { get; set; }
    public bool SelectionEnabled { get; set; }
    public string SourceId { get; set; } = "lmt";
    public string TargetLanguage { get; set; } = "en";
    public int StableMs { get; set; } = 300;
    public int MaxWaitMs { get; set; } = 1000;
    public int DeadlineMs { get; set; } = 3000;
    public double? Right { get; set; }
    public double? Bottom { get; set; }
    public double Width { get; set; } = 420;
    public double Height { get; set; } = 180;
    public bool Enabled => SentenceEnabled || SelectionEnabled;
}
public record ResourceSettings
{
    public int ModelLoadTimeoutSeconds { get; set; } = 60;
    public int IdleUnloadMinutes { get; set; } = 10;
}
public record StartupSettings
{
    public bool CloseToTray { get; set; } = true;
    public bool RunAtLogin { get; set; }
    public bool PreloadModel { get; set; }
}
public record ProviderSettings
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public ProviderKind Kind { get; set; }
    private bool? _enabled;
    public bool Enabled { get => _enabled ?? Kind != ProviderKind.OnlineModel; set => _enabled = value; }
    public List<string> SupportedTargets { get; set; } = ["en"];
    public string DictionaryPath { get; set; } = "";
    public string Endpoint { get; set; } = "http://127.0.0.1:18089/";
    public string ApiKeyEnvironmentVariable { get; set; } = "";
    public string Model { get; set; } = "NiuTrans/LMT-60-1.7B";
    public string ModelRevision { get; set; } = "unverified";
    public string Quantization { get; set; } = "Q4_K_M";
    public string ModelSha256 { get; set; } = "";
    public bool Managed { get; set; }
    public string ExecutablePath { get; set; } = "";
    public string CpuExecutablePath { get; set; } = "";
    public string ModelPath { get; set; } = "";
    public bool UseInstalledModel { get; set; }
    public bool UseGpu { get; set; } = true;
    public int ContextTokens { get; set; } = 512;
    public int InputTokenBudget { get; set; } = 448;
    public int MaxOutputTokens { get; set; } = 32;
    public bool LlamaApi { get; set; } = true;
    public string Prompt { get; set; } = PromptTemplate.Default;
}
public record ColumnSettings
{
    public string Id { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string SourceId { get; set; } = "";
    public string TargetLanguage { get; set; } = "en";
    public List<string> FallbackSourceIds { get; set; } = [];
    public string? PromptOverride { get; set; }
    public string ColorLight { get; set; } = "#2266CC";
    public string ColorDark { get; set; } = "#8DBBFF";
    public int Width { get; set; } = 180;
}
public static class SettingsJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() }, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public static AppSettings Clone(AppSettings value) => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(value, Options), Options)!;
    public static AppSettings Load(string path)
    {
        var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("Empty settings");
        SettingsValidator.ThrowIfInvalid(s); return s;
    }
    public static void Save(string path, AppSettings settings)
    {
        SettingsValidator.ThrowIfInvalid(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
public static class SettingsValidator
{
    public static IReadOnlyList<string> Validate(AppSettings s)
    {
        var errors = new List<string>();
        void Check(bool ok, string message) { if (!ok) errors.Add(message); }
        void Range(int value, int min, int max, string field) => Check(value >= min && value <= max, $"{field}: {min}–{max}");
        Check(s.SchemaVersion == 1, "不支持的 schemaVersion");
        if (s.Capture is null || s.Response is null || s.Display is null || s.TextTranslation is null || s.Resources is null || s.Startup is null || s.Providers is null || s.Columns is null)
            return ["配置分组不可为 null"];
        Range(s.Capture.PollMs, 25, 1000, "pollMs"); Range(s.Capture.WatchdogMs, 100, 5000, "watchdogMs");
        Range(s.Capture.MaxCandidates, 1, 10, "maxCandidates"); Range(s.Response.StableMs, 0, 2000, "stableMs");
        Range(s.Response.SnapshotDeadlineMs, 100, 30000, "snapshotDeadlineMs");
        Range(s.Response.LocalStallMs, 50, 30000, "localStallMs"); Range(s.Response.OnlineStallMs, 50, 30000, "onlineStallMs");
        Range(s.Response.DictionaryTimeoutMs, 10, 1000, "dictionaryTimeoutMs"); Range(s.Response.CancelGraceMs, 0, 3000, "cancelGraceMs");
        Range(s.Response.MemoryCacheEntries, 0, 10000, "memoryCacheEntries");
        Range(s.Resources.ModelLoadTimeoutSeconds, 5, 600, "modelLoadTimeoutSeconds"); Range(s.Resources.IdleUnloadMinutes, 0, 1440, "idleUnloadMinutes");
        Check(double.IsFinite(s.Display.MaxWidthRatio) && s.Display.MaxWidthRatio >= .2 && s.Display.MaxWidthRatio <= .8, "maxWidthRatio: 0.2–0.8");
        Range(s.Display.OffsetX, -1000, 1000, "offsetX"); Range(s.Display.OffsetY, 0, 1000, "offsetY");
        Check(s.Providers.Count is > 0 and <= 16 && s.Providers.All(x => x != null), "来源数量必须为 1–16，且非 null");
        Check(s.Columns.Count is > 0 and <= 8 && s.Columns.All(x => x != null), "列数量必须为 1–8，且非 null");
        if (s.Providers.Any(x => x == null) || s.Columns.Any(x => x == null)) return errors;
        var text = s.TextTranslation;
        Range(text.StableMs, 0, 2000, "text.stableMs"); Range(text.MaxWaitMs, Math.Max(1, text.StableMs), 5000, "text.maxWaitMs");
        Range(text.DeadlineMs, 100, 30000, "text.deadlineMs");
        Check(double.IsFinite(text.Width) && text.Width >= 240 && text.Width <= 1600 && double.IsFinite(text.Height) && text.Height >= 100 && text.Height <= 1200, "正文窗口尺寸无效");
        Check((text.Right == null || double.IsFinite(text.Right.Value)) && (text.Bottom == null || double.IsFinite(text.Bottom.Value)), "正文窗口位置无效");
        if (text.Enabled)
        {
            var model = s.Providers.FirstOrDefault(p => p.Id == text.SourceId);
            Check(model?.Kind == ProviderKind.LocalModel && model.SupportedTargets.Contains(text.TargetLanguage), "正文翻译需要支持目标语言的本地模型");
            Check(model == null || model.ContextTokens >= 256, "正文模型上下文不足");
        }
        Check(s.Providers.Select(x => x.Id).Distinct().Count() == s.Providers.Count, "来源 ID 重复");
        Check(s.Columns.Select(x => x.Id).Distinct().Count() == s.Columns.Count, "列 ID 重复");
        foreach (var p in s.Providers)
        {
            Check(ValidId(p.Id), "来源 ID 只允许英文字母、数字、下划线与连字符");
            Check(Enum.IsDefined(p.Kind), "未知来源类型");
            if (p.SupportedTargets is null || p.SupportedTargets.Count == 0) { errors.Add($"{p.Id}: 缺少语言方向"); continue; }
            foreach (var lang in p.SupportedTargets) { try { _ = LanguageCatalog.Name(lang); } catch { errors.Add($"{p.Id}: 无效语言代码"); } }
            if (p.Kind != ProviderKind.LocalDictionary)
            {
                try { _ = PromptTemplate.Render(p.Prompt, "苹果", "en"); } catch (Exception ex) { errors.Add(ex.Message); }
                Range(p.ContextTokens, 128, 32768, "contextTokens"); Range(p.MaxOutputTokens, 1, 64, "maxOutputTokens");
                Check(p.InputTokenBudget > 0 && p.InputTokenBudget + p.MaxOutputTokens + 16 <= p.ContextTokens, "输入、输出与控制 token 超出上下文");
                Check(Uri.TryCreate(p.Endpoint, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https") && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query), "Endpoint 必须是无凭据/查询参数的 HTTP(S) 地址");
                if (uri != null && p.Kind == ProviderKind.LocalModel) Check(uri.IsLoopback, "本地模型只能使用回环地址");
                if (uri != null && p.Kind == ProviderKind.OnlineModel) Check(uri.Scheme == "https", "在线模型必须使用 HTTPS");
                Check(!p.Managed || p.Kind == ProviderKind.LocalModel && p.LlamaApi, "托管只支持本地 llama.cpp");
                if (p.Managed && p.Enabled) Check(!string.IsNullOrWhiteSpace(p.ExecutablePath) && !string.IsNullOrWhiteSpace(p.ModelPath), "托管模型需要 llama-server 和 GGUF 路径");
            }
        }
        foreach (var c in s.Columns)
        {
            Check(ValidId(c.Id), "列 ID 无效"); Range(c.Width, 80, 400, "列宽");
            Check(ValidColor(c.ColorLight) && ValidColor(c.ColorDark), "颜色必须为 #RRGGBB");
            var source = s.Providers.FirstOrDefault(p => p.Id == c.SourceId);
            Check(source != null && source.SupportedTargets?.Contains(c.TargetLanguage) == true, $"{c.Id}: 来源不支持目标语言");
            if (c.FallbackSourceIds is null) { errors.Add($"{c.Id}: fallback 不可为 null"); continue; }
            foreach (var id in c.FallbackSourceIds)
            {
                var fallback = s.Providers.FirstOrDefault(p => p.Id == id);
                Check(fallback != null && fallback.SupportedTargets?.Contains(c.TargetLanguage) == true && fallback.Kind == ProviderKind.LocalDictionary,
                    $"{c.Id}: fallback 必须是同语言本地词典");
            }
            if (c.PromptOverride != null) { try { _ = PromptTemplate.Render(c.PromptOverride, "苹果", c.TargetLanguage); } catch (Exception ex) { errors.Add(ex.Message); } }
        }
        return errors;
    }
    private static bool ValidId(string? value) => value is { Length: > 0 and <= 64 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    private static bool ValidColor(string? value) => value is { Length: 7 } && value[0] == '#' && value[1..].All(Uri.IsHexDigit);
    public static void ThrowIfInvalid(AppSettings s) { var errors = Validate(s); if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors)); }
}
