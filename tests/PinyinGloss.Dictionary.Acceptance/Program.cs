using System.Diagnostics;
using System.Text.Json;
using PinyinGloss.Core;
using PinyinGloss.Providers;

// 显式运行的完整外部词库验收；普通测试和 CI 不下载第三方数据。
if (args.Length != 2) throw new ArgumentException("参数：<CC-CEDICT TSV 绝对路径> <验收输出目录>");
var tsv = Path.GetFullPath(args[0]); var directory = Path.GetFullPath(args[1]);
Directory.CreateDirectory(directory);
var db = Path.Combine(directory, "cc-cedict.db");
var watch = Stopwatch.StartNew();
var manifest = await DictionaryStore.ImportAsync(tsv, db, "CC-CEDICT", "CC-BY-SA-4.0",
    "CC-CEDICT contributors / MDBG; https://www.mdbg.net/chinese/dictionary?page=cedict; converted by scripts/Convert-Cedict.ps1", default);
Console.WriteLine($"PASS 导入：{JsonSerializer.Serialize(manifest)} 耗时ms={watch.ElapsedMilliseconds}");
void Check(bool condition, string detail) { if (!condition) throw new Exception(detail); Console.WriteLine("PASS " + detail); }
foreach (var (word, fragment) in new[] { ("苹果", "apple"), ("蘋果", "apple"), ("保险", "insurance"), ("保險", "insurance"), ("保鲜", "fresh"), ("保鮮", "fresh"), ("支持", "support") })
{
    var gloss = await DictionaryStore.LookupAsync(db, word, "en", default);
    Check(gloss?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true, $"查词 {word}: {gloss}");
}
Check(await DictionaryStore.LookupAsync(db, "苹果", "ja", default) == null, "日文不混入英文");
Check(await DictionaryStore.LookupAsync(db, "这是不存在的验收测试词头XYZ", "en", default) == null, "缺词不杜撰释义");
var bad = Path.Combine(directory, "invalid.tsv"); await File.WriteAllTextAsync(bad, "invalid\n");
try { await DictionaryStore.ImportAsync(bad, db, "bad", "MIT", "authored test fixture", default); throw new Exception("损坏导入意外成功"); }
catch (InvalidDataException) { Console.WriteLine("PASS 拒绝损坏导入"); }
Check(DictionaryStore.ReadManifest(db).Sha256 == manifest.Sha256, "失败导入保留原清单");
Check((await DictionaryStore.LookupAsync(db, "苹果", "en", default))?.Contains("apple") == true, "失败导入保留原词条");
var demoTsv = Path.Combine(directory, "comparison.tsv"); var demo = Path.Combine(directory, "comparison.db");
await File.WriteAllTextAsync(demoTsv, "苹果\ten\tapple\n验收兜底测试\ten\tfallback test\n");
await DictionaryStore.ImportAsync(demoTsv, demo, "原创验收对照", "MIT", "Pinyin Gloss authors", default);
var jaTsv = Path.Combine(directory, "comparison-ja.tsv"); var jaDb = Path.Combine(directory, "comparison-ja.db");
await File.WriteAllTextAsync(jaTsv, "苹果\tja\tりんご\n保险\tja\t保険\n");
await DictionaryStore.ImportAsync(jaTsv, jaDb, "原创日文验收（2词条）", "MIT", "Pinyin Gloss authors; authored acceptance fixture, not a complete Japanese dictionary", default);
var settings = AppSettings.CreateDefault(); settings.Startup.PreloadModel = false;
settings.Providers = [new() { Id="cedict", Name="CC-CEDICT", Kind=ProviderKind.LocalDictionary, DictionaryPath=db, SupportedTargets=["en"] }, new() { Id="comparison", Name="原创验收对照", Kind=ProviderKind.LocalDictionary, DictionaryPath=demo, SupportedTargets=["en"] }];
settings.Columns = [new() { Id="cedict-en", SourceId="cedict", TargetLanguage="en", FallbackSourceIds=["comparison"], Width=300, ColorLight="#23834B", ColorDark="#7ACD91" }, new() { Id="comparison-en", SourceId="comparison", TargetLanguage="en", FallbackSourceIds=["cedict"], Width=220 }];
settings.Providers.Add(new() { Id="comparison-ja", Name="原创日文验收（2词条）", Kind=ProviderKind.LocalDictionary, DictionaryPath=jaDb, SupportedTargets=["ja"] });
settings.Columns.Add(new() { Id="comparison-ja", SourceId="comparison-ja", TargetLanguage="ja", Width=180, ColorLight="#995B16", ColorDark="#E5BB80" });
SettingsValidator.ThrowIfInvalid(settings);
var providers = ProviderFactory.Create(settings);
using (var coordinator = new TranslationCoordinator(providers, settings))
{
    var cells = new Dictionary<(string, string), TranslationCell>();
    coordinator.CellUpdated += cell => cells[(cell.Result.OriginalText,cell.ColumnId)]=cell;
    coordinator.Submit(new("dictionary-test", "dictionary-test-1", 1, "test", new(0,0,400,40), [new("apple", "苹果",1), new("fallback", "验收兜底测试",2)]));
    await coordinator.WhenIdleAsync();
    foreach (var column in settings.Columns.Where(c => c.TargetLanguage == "en"))
        Check(cells.TryGetValue(("苹果",column.Id),out var cell) && cell.Result.Status == ResultStatus.Ready && cell.Result.Gloss?.Contains("apple",StringComparison.OrdinalIgnoreCase)==true, "并排来源 " + column.Id);
    Check(cells[("验收兜底测试","cedict-en")].Result.Gloss == "fallback test" && cells[("验收兜底测试","cedict-en")].ActualSourceId == "comparison", "本地词典同语言兜底标明实际来源");
    Check(cells[("苹果","comparison-ja")].Result.Gloss == "りんご", "日文列查询自身词典");
    Check(cells[("验收兜底测试","comparison-ja")].Result.Status == ResultStatus.NotFound, "日文缺词不混入英文兜底");
}
SettingsJson.Save(Path.Combine(directory,"dictionary-only.settings.json"), settings);
var times = new List<double>();
for (var i=0;i<40;i++) { watch.Restart(); await DictionaryStore.LookupAsync(db,"保险","en",default); times.Add(watch.Elapsed.TotalMilliseconds); }
times.Sort(); Console.WriteLine($"MEASURE 纯查词 n=40 p50Ms={times[19]:F3} p95Ms={times[37]:F3} maxMs={times[^1]:F3}；不代表端到端延迟");

// 后台调度性能：真实词典、三列七候选，含缺词，不创建窗口、不读取用户桌面。
var measured = SettingsJson.Clone(settings); measured.Response.StableMs = 2000;
measured.Response.SnapshotDeadlineMs = 3000; measured.Response.MemoryCacheEntries = 0; measured.Capture.MaxCandidates = 7;
using (var coordinator = new TranslationCoordinator(providers, measured))
{
    var completed = 0; coordinator.CellUpdated += _ => completed++;
    times.Clear();
    for (var i = 0; i < 40; i++)
    {
        var words = new[] { "保鲜", "保险", "包线", "包馅", "报险", "爆线", "报" };
        watch.Restart(); coordinator.Submit(new("perf", $"perf-{i}", i, "test", new(0, 0, 400, 40),
            words.Select((word, index) => new Candidate($"word-{index}", word, index + 1)).ToArray()));
        await coordinator.WhenIdleAsync(); times.Add(watch.Elapsed.TotalMilliseconds);
    }
    Check(completed == 40 * 7 * 3, "三列七候选共 840 个终态，无缺失或重复");
    Check(times.Max() < 1000, "纯词典完整页不等待 2000ms 模型稳定阈值");
    times.Sort(); Console.WriteLine($"MEASURE 后台整页调度 n=40 cols=3 candidates=7 cache=off stableMs=2000 p50Ms={times[19]:F3} p95Ms={times[37]:F3} maxMs={times[^1]:F3}；不含 UIA/窗口/屏幕合成，不与屏幕采样口径直接比较");
}
