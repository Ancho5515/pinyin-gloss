using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using PinyinGloss.Core;
using PinyinGloss.Providers;

// 显式运行的真实权重后台验收，不创建窗口、不读取桌面、不修改用户配置。
if (args.Length is < 2 or > 3) { Console.Error.WriteLine("用法：Model.Acceptance <安装清单所在目录> <结果目录> [诊断用取消宽限ms|missing-gpu-runtime]"); return 2; }
var missingGpuRuntime = args.Length == 3 && args[2] == "missing-gpu-runtime";
var promptProbe = args.Length == 3 && args[2] == "prompt-probe";
var textProbe = args.Length == 3 && args[2] == "text-probe";
var install = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
await using var log = new StreamWriter(Path.Combine(output, "results.jsonl"), false) { AutoFlush = true };
void Record(object value) { var json = JsonSerializer.Serialize(value); log.WriteLine(json); Console.WriteLine(json); }
void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); Record(new { kind = "pass", name }); }
try
{
    var settings = AppSettings.CreateDefault();
    if (args.Length == 3 && !missingGpuRuntime && !promptProbe && !textProbe) settings.Response.CancelGraceMs = int.Parse(args[2]);
    Check(LlmInstallation.TryApply(settings, install, true, out var error), error ?? "自动读取安装清单");
    var config = settings.Providers[0];
    if (missingGpuRuntime)
    {
        Check(config.UseGpu && File.Exists(config.CpuExecutablePath), "GPU 优先且有独立 CPU 备用包");
        // 故障注入：只复制官方 CUDA 启动器，不复制其实现 DLL，不修改正常安装或系统。
        var broken = Path.Combine(output, "missing-gpu-dll"); Directory.CreateDirectory(broken);
        var executable = Path.Combine(broken, "llama-server.exe"); File.Copy(config.ExecutablePath, executable, true);
        config.ExecutablePath = executable;
    }
    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
    config.Endpoint = $"http://127.0.0.1:{port}/";
    var existing = Process.GetProcessesByName("llama-server").Select(p => p.Id).ToHashSet();
    var server = new ManagedLlamaServer(config, 60);
    await using var provider = new HttpModelProvider(config, settings.Response, managedServer: server);
    var watch = Stopwatch.StartNew(); await provider.StartAsync(default);
    Check(server.Ready, "模型真实加载：" + provider.State);
    if (config.UseGpu) await File.WriteAllTextAsync(Path.Combine(output, "gpu-startup.log"), server.GpuStartupDiagnostics);
    var pid = Process.GetProcessesByName("llama-server").Single(p => !existing.Contains(p.Id)).Id;
    Record(new { kind = "load", milliseconds = watch.Elapsed.TotalMilliseconds, pid, config.Model, config.Quantization, config.ModelRevision, config.ModelSha256, config.UseGpu,
        server.ActiveBackend, server.OffloadedLayers, server.TotalLayers, server.FallbackReason, provider.State });
    Check(!config.UseGpu || server.ActiveBackend == "Gpu" && server.OffloadedLayers > 0 || server.ActiveBackend == "Cpu" && server.FallbackReason != null,
        "实际 GPU 层卸载或明确 CPU 回退原因");
    if (missingGpuRuntime) Check(server.ActiveBackend == "Cpu" && server.FallbackReason != null, "GPU 启动器缺少 DLL 后真实回退独立 CPU 运行时");
    using var client = new HttpClient { BaseAddress = new(config.Endpoint), Timeout = TimeSpan.FromSeconds(2) };
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.ApiKey);
    async Task<bool> Busy()
    {
        using var response = await client.GetAsync("slots"); response.EnsureSuccessStatusCode();
        using var slots = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return slots.RootElement.GetArrayLength() == 1 && slots.RootElement[0].GetProperty("is_processing").GetBoolean();
    }
    async Task<ProviderResult> Translate(string text, string language, int budget = 5000)
    {
        var timer = Stopwatch.StartNew();
        var result = await provider.QueryAsync(new(new("word", text, 1), language, PromptTemplate.Render(config.Prompt, text, language), budget), default);
        Record(new { kind = "translation", text, language, milliseconds = timer.Elapsed.TotalMilliseconds, status = result.Status.ToString(), result.Gloss, result.ErrorCode });
        return result;
    }
    if (textProbe)
    {
        const string multiline = "这是第一句。\n这是第二句。";
        foreach (var input in new[] { multiline, multiline.Replace('\n', ' ') })
        {
            var result = await provider.QueryAsync(new(new("probe", multiline, 1), "en", PromptTemplate.Render("Translate the following text from {{source_language_name}} into {{target_language_name}}:\n{{source_language_name}}: {{candidate}}\n{{target_language_name}}:", input, "en"), 3000, 128, false), default);
            Record(new { kind = "text-prompt-probe", input, result.Gloss, result.Status, result.ErrorCode });
        }
        settings.TextTranslation.SentenceEnabled = settings.TextTranslation.SelectionEnabled = true;
        settings.TextTranslation.SourceId = config.Id;
        settings.TextTranslation.DeadlineMs = 3000;
        using var textCoordinator = new TranslationCoordinator(new Dictionary<string, ITranslationProvider> { [config.Id] = provider }, settings);
        var textExamples = new[]
        {
            ("我喜欢苹果。", TextMode.Sentence, "apple"),
            ("今天的天气很好。", TextMode.Sentence, "weather"),
            ("请把这份文件发给我。", TextMode.Sentence, "file|document"),
            ("这家银行周末不开门。", TextMode.Sentence, "bank"),
            (multiline, TextMode.Selection, "first;second")
        };
        var timings = new List<double>(); var version = 0;
        for (var round = 0; round < 3; round++)
            foreach (var (text, mode, expected) in textExamples)
            {
                var snapshot = new TextSnapshot("text-acceptance", ++version, 1, "fixture", mode, text);
                var timer = Stopwatch.StartNew(); var result = await textCoordinator.QueryTextAsync(snapshot, default);
                timings.Add(timer.Elapsed.TotalMilliseconds);
                Record(new { kind = "text-translation", round, mode = mode.ToString(), text, result.Gloss, result.ErrorCode, status = result.Status.ToString(), milliseconds = timings[^1] });
                Check(result.Status == ResultStatus.Ready && expected.Split(';').All(group => group.Split('|').Any(word => result.Gloss!.Contains(word, StringComparison.OrdinalIgnoreCase))), "正文样例输出与基本词义");
                Check(!Process.GetProcessById(pid).HasExited && !await Busy(), "正文使用同一模型进程且单槽空闲");
            }
        var overBudget = await textCoordinator.QueryTextAsync(new("text-acceptance", ++version, 1, "fixture", TextMode.Selection, new string('龘', 256)), default);
        Record(new { kind = "text-budget", status = overBudget.Status.ToString(), overBudget.ErrorCode });
        Check(overBudget.Status == ResultStatus.InvalidOutput && overBudget.ErrorCode == "prompt_token_budget", "256 字符以内但真实分词超预算时拒绝，不静默截断");
        for (var iteration = 0; iteration < 3; iteration++)
        {
            using var cancellation = new CancellationTokenSource();
            var timer = Stopwatch.StartNew();
            var task = textCoordinator.QueryTextAsync(new("text-acceptance", ++version, 1, "fixture", TextMode.Sentence, string.Concat(Enumerable.Repeat("这段文字用于验证取消后模型继续翻译。", 8))), cancellation.Token);
            var busy = false;
            while (!task.IsCompleted && timer.ElapsedMilliseconds < 1500)
            { if (await Busy()) { busy = true; break; } await Task.Delay(5); }
            var cancelAt = timer.Elapsed.TotalMilliseconds; cancellation.Cancel();
            try { await task; } catch (OperationCanceledException) { }
            // 调度器先使调用者取消，实际 SSE 清理完成前仍持有槽位。
            while (server.Ready && await Busy() && timer.Elapsed.TotalMilliseconds - cancelAt < settings.Response.CancelGraceMs + 500) await Task.Delay(10);
            Record(new { kind = "text-cancel-state", iteration, busyObserved = busy, server.Ready, provider.State });
            Check(busy && server.Ready && !Process.GetProcessById(pid).HasExited && !await Busy(), "正文取消后同 PID 复用、单槽清空");
            Record(new { kind = "text-cancel", iteration, pid, cleanupMs = timer.Elapsed.TotalMilliseconds - cancelAt });
        }
        timings.Sort();
        using var textProcess = Process.GetProcessById(pid);
        Record(new { kind = "text-timing", n = timings.Count, p50 = timings[(int)Math.Ceiling(timings.Count * .5) - 1], p95 = timings[(int)Math.Ceiling(timings.Count * .95) - 1], max = timings[^1], modelWorkingSetBytes = textProcess.WorkingSet64 });
        await provider.StopAsync(default); Check(!server.Ready, "正文验收后释放模型");
        Record(new { kind = "complete", result = "PASS", scope = "后台正文请求，不含 UIA、合并等待和屏幕；仅五个基本样例，不代表完整语言质量" }); return 0;
    }
    if (promptProbe)
    {
        var templates = new[] { ("short-word", config.Prompt), ("official", "Translate the following text from {{source_language_name}} into {{target_language_name}}:\n{{source_language_name}}: {{candidate}}\n{{target_language_name}}:") };
        foreach (var (name, template) in templates)
        {
            config.Prompt = template; Record(new { kind = "probe-template", name, template });
            foreach (var word in new[] { "平果", "平果市", "苹果", "保险", "保鲜", "包线", "银行", "支持", "你好" })
                foreach (var language in new[] { "en", "ja" }) await Translate(word, language);
        }
        await provider.StopAsync(default); Check(!server.Ready, "提示词对照后释放模型");
        Record(new { kind = "complete", result = "PASS", scope = "仅提示词对照采集，不宣称语义质量通过" }); return 0;
    }
    var examples = new[] { ("苹果", "en", "apple"), ("保险", "en", "insurance"), ("你好", "en", "hello|hi"),
        ("苹果", "ja", "リンゴ|りんご|林檎"), ("保险", "ja", "保険"), ("你好", "fr", "bonjour|salut") };
    foreach (var (text, language, expected) in examples)
    {
        var result = await Translate(text, language);
        Check(result.Status == ResultStatus.Ready, $"{text}/{language} 输出格式");
        Check(expected.Split('|').Any(word => word == "hi" ? result.Gloss!.Trim().Trim('.', '!', '?').Equals("hi", StringComparison.OrdinalIgnoreCase) :
            result.Gloss!.Contains(word, StringComparison.OrdinalIgnoreCase)), $"{text}/{language} 基本词义");
    }
    // 必须观察到真实后端忙，再取消。取消后确认仍是同一个 PID，后续生成正常。
    for (var iteration = 0; iteration < 5; iteration++)
    {
        using var cancellation = new CancellationTokenSource();
        var text = "这段文字用于测试翻译请求取消后，后台模型是否释放计算槽位并保持加载，以便新的候选可以继续获得完整准确的翻译。";
        var timer = Stopwatch.StartNew();
        var task = provider.QueryAsync(new(new("cancel", text, 1), "en", PromptTemplate.Render(config.Prompt, text, "en"), 5000), cancellation.Token);
        var busy = false;
        while (!task.IsCompleted && timer.ElapsedMilliseconds < 1500)
        { if (await Busy()) { busy = true; break; } await Task.Delay(5); }
        var cancelAt = timer.Elapsed.TotalMilliseconds; cancellation.Cancel();
        try { await task; } catch (OperationCanceledException) { }
        Record(new { kind = "cancel-state", iteration, ready = server.Ready, provider.State, cancelGraceMs = settings.Response.CancelGraceMs, cleanupMs = timer.Elapsed.TotalMilliseconds - cancelAt });
        Check(busy, "已观察真实生成槽位忙");
        Check(server.Ready && !Process.GetProcessById(pid).HasExited && !await Busy(), "取消后同一进程就绪且槽位空闲");
        Record(new { kind = "cancel", iteration, pid, milliseconds = timer.Elapsed.TotalMilliseconds });
        Check((await Translate("苹果", "en")).Status == ResultStatus.Ready, "取消后继续翻译");
    }
    // 生产调度、默认期限；注入快照仅验证后端，不能当作真实微软拼音验收。
    settings.Response.MemoryCacheEntries = 0;
    settings.Providers = [config]; settings.Columns = [new() { Id = "en", SourceId = "lmt", TargetLanguage = "en" }, new() { Id = "ja", SourceId = "lmt", TargetLanguage = "ja" }];
    using var coordinator = new TranslationCoordinator(new Dictionary<string, ITranslationProvider> { ["lmt"] = provider }, settings);
    var cells = new List<TranslationCell>();
    for (var page = 0; page < 3; page++)
    {
        cells.Clear(); var timer = Stopwatch.StartNew();
        var initial = new HashSet<(string, string)>(); double? initialPageMs = null, firstGlossMs = null;
        void OnCell(TranslationCell cell)
        {
            cells.Add(cell); initial.Add((cell.ColumnId, cell.Result.CandidateId));
            if (initial.Count == 10) initialPageMs ??= timer.Elapsed.TotalMilliseconds;
            if (cell.Result.Status == ResultStatus.Ready) firstGlossMs ??= timer.Elapsed.TotalMilliseconds;
        }
        coordinator.CellUpdated += OnCell;
        var words = new[] { "苹果", "保险", "保鲜", "支持", "银行" };
        var snapshot = new CandidateSnapshot("model-test", $"page-{page}", page, "injected-backend-only", new(0, 0, 100, 30), words.Select((w, i) => new Candidate(i.ToString(), w, i + 1)).ToArray());
        coordinator.Submit(snapshot); await coordinator.WhenIdleAsync();
        coordinator.CellUpdated -= OnCell;
        var final = cells.Where(c => c.Result.Status != ResultStatus.Pending).ToArray();
        Check(final.Select(c => (c.ColumnId, c.Result.CandidateId)).Distinct().Count() == 10, "整页所有单元格都有终态");
        Record(new { kind = "page", page, milliseconds = timer.Elapsed.TotalMilliseconds,
            ready = final.Count(c => c.Result.Status == ResultStatus.Ready), timeout = final.Count(c => c.Result.Status == ResultStatus.Timeout),
            initialPageMs, firstGlossMs, final = final.Select(c => new { c.ColumnId, c.Result.OriginalText, status = c.Result.Status.ToString(), c.Result.Gloss }) });
        // 调度器超时返回可能早于 provider 的取消清理完成，等待其确认后才进入下一轮验收。
        await Task.Delay(settings.Response.CancelGraceMs + 100);
        Check(server.Ready && !await Busy(), "默认期限后槽位已释放且模型仍加载");
    }
    using var process = Process.GetProcessById(pid); process.Refresh();
    Record(new { kind = "resources", process.WorkingSet64, process.PeakWorkingSet64, process.PrivateMemorySize64, cpuSeconds = process.TotalProcessorTime.TotalSeconds });
    await provider.StopAsync(default); Check(!server.Ready, "显式释放模型");
    Record(new { kind = "complete", result = "PASS", config.Quantization, server.ActiveBackend, server.OffloadedLayers,
        scope = "真实模型后台验收；没有桌面或完整语言质量验收" }); return 0;
}
catch (Exception ex) { Record(new { kind = "failure", error = ex.ToString() }); return 1; }
