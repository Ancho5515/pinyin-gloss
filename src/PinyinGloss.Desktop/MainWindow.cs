using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using PinyinGloss.Core;
using PinyinGloss.Providers;

namespace PinyinGloss.Desktop;
internal sealed class MainWindow : Window
{
    private readonly DesktopRuntime _runtime; private AppSettings _draft;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 10) };
    private readonly TabControl _tabs = new(); private readonly List<Action> _writeFields = [];
    private DataGrid? _columns; private ColumnSettings? _selectedColumn; private bool _saving;
    private long _draftGeometryRevision;
    public MainWindow(DesktopRuntime runtime)
    {
        _runtime = runtime; _draft = SettingsJson.Clone(runtime.Settings);
        _draftGeometryRevision = runtime.GeometryRevision;
        Title = "Pinyin Gloss · 微软拼音翻译外挂（实验版）"; Width = 1000; Height = 740; MinWidth = 780; MinHeight = 580;
        FontFamily = new("Microsoft YaHei UI"); FontSize = 14; Background = new SolidColorBrush(Color.FromRgb(246, 248, 251));
        var layout = new DockPanel { Margin = new(22) };
        var title = new TextBlock { Text = "Pinyin Gloss", FontSize = 26, FontWeight = FontWeights.SemiBold }; DockPanel.SetDock(title, Dock.Top); layout.Children.Add(title);
        var subtitle = new TextBlock { Text = "保留微软拼音的输入、候选和记忆，在旁边显示翻译。", Margin = new(0, 5, 0, 10) }; DockPanel.SetDock(subtitle, Dock.Top); layout.Children.Add(subtitle);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); footer.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; buttons.Children.Add(Button("保存并应用", async () => await SaveAsync()));
        buttons.Children.Add(Button("重新载入", () => { _draft = SettingsJson.Clone(_runtime.Settings); _draftGeometryRevision = _runtime.GeometryRevision; BuildTabs(); }));
        buttons.Children.Add(Button("退出程序", _runtime.Exit)); footer.Children.Add(buttons); layout.Children.Add(footer); layout.Children.Add(_tabs); Content = layout;
        _runtime.StatusChanged += text => _status.Text = text + "\n" + _runtime.TextReadStatus + (string.IsNullOrEmpty(_runtime.ModelStatus) ? "" : "\n" + _runtime.ModelStatus);
        _status.Text = _runtime.Status; BuildTabs();
        Closing += (_, e) => { if (!_runtime.ExitRequested && _runtime.Settings.Startup.CloseToTray) { e.Cancel = true; Hide(); } else if (!_runtime.ExitRequested) { e.Cancel = true; Dispatcher.BeginInvoke(_runtime.Exit); } };
    }
    private static Button Button(string text, Action action) { var button = new Button { Content = text }; button.Click += (_, _) => { try { action(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Pinyin Gloss"); } }; return button; }
    private static Button Button(string text, Func<Task> action) { var button = new Button { Content = text }; button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Pinyin Gloss"); } }; return button; }
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(0, 7, 0, 12), Foreground = Brushes.DimGray };
    private StackPanel Tab(string name)
    {
        var panel = new StackPanel { Margin = new(16) }; _tabs.Items.Add(new TabItem { Header = name, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }); return panel;
    }
    private void Integer(StackPanel panel, string title, Func<int> getter, Action<int> setter)
    {
        panel.Children.Add(new TextBlock { Text = title }); var text = new TextBox { Text = getter().ToString(), MaxWidth = 200, HorizontalAlignment = HorizontalAlignment.Left }; panel.Children.Add(text);
        _writeFields.Add(() => { if (!int.TryParse(text.Text, out var value)) throw new ArgumentException(title + "需要整数"); setter(value); });
    }
    private static CheckBox Toggle(StackPanel panel, string title, bool value, Action<bool> setter)
    {
        var check = new CheckBox { Content = title, IsChecked = value }; check.Checked += (_, _) => setter(true); check.Unchecked += (_, _) => setter(false); panel.Children.Add(check);
        return check;
    }
    private static TextBox TextField(StackPanel panel, string title, string value, Action<string> setter, bool multi = false)
    {
        panel.Children.Add(new TextBlock { Text = title }); var text = new TextBox { Text = value, AcceptsReturn = multi, MinHeight = multi ? 90 : 0, TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap };
        text.TextChanged += (_, _) => setter(text.Text); panel.Children.Add(text); return text;
    }
    private void BuildTabs()
    {
        _writeFields.Clear(); _tabs.Items.Clear(); _selectedColumn = null;
        var overview = Tab("概览"); overview.Children.Add(Note("首次使用先运行候选诊断。UIA 读取仍需在你的 Windows / 应用上验证；没有读取到候选时，微软拼音照常工作。"));
        Toggle(overview, "启用翻译外挂", _draft.Enabled, value => _draft.Enabled = value);
        overview.Children.Add(Button("运行真实候选诊断（60 秒）", _runtime.RunDiagnostic));
        overview.Children.Add(Button("显示模拟候选演示", _runtime.ShowDemo)); overview.Children.Add(Button("退出演示，恢复真实读取", _runtime.EndDemo));
        overview.Children.Add(Note("模拟候选固定为苹果、保险、支持，只用于检验词典、模型、布局与设置。它不能证明微软候选可读。"));
        overview.Children.Add(Button("重启候选读取", _runtime.RestartReaderAsync));
        var response = Tab("响应 / 资源");
        Integer(response, "候选稳定阈值（ms）", () => _draft.Response.StableMs, n => _draft.Response.StableMs = n);
        Integer(response, "快照总期限（ms，包含排队与推理）", () => _draft.Response.SnapshotDeadlineMs, n => _draft.Response.SnapshotDeadlineMs = n);
        Integer(response, "本地模型无进展阈值（ms）", () => _draft.Response.LocalStallMs, n => _draft.Response.LocalStallMs = n);
        Integer(response, "在线模型无进展阈值（ms）", () => _draft.Response.OnlineStallMs, n => _draft.Response.OnlineStallMs = n);
        response.Children.Add(Note($"当前在线有效最长等待：{Math.Min(_draft.Response.OnlineStallMs, _draft.Response.SnapshotDeadlineMs)} ms（还会扣除排队耗时）。"));
        Integer(response, "模型取消宽限（ms）", () => _draft.Response.CancelGraceMs, n => _draft.Response.CancelGraceMs = n);
        Integer(response, "词典慢查询阈值（ms）", () => _draft.Response.DictionaryTimeoutMs, n => _draft.Response.DictionaryTimeoutMs = n);
        Integer(response, "读取轮询间隔（ms）", () => _draft.Capture.PollMs, n => _draft.Capture.PollMs = n);
        Integer(response, "UIA 看门狗（ms）", () => _draft.Capture.WatchdogMs, n => _draft.Capture.WatchdogMs = n);
        Integer(response, "最多可见候选", () => _draft.Capture.MaxCandidates, n => _draft.Capture.MaxCandidates = n);
        Integer(response, "模型加载期限（秒）", () => _draft.Resources.ModelLoadTimeoutSeconds, n => _draft.Resources.ModelLoadTimeoutSeconds = n);
        Integer(response, "模型空闲卸载（分钟，0 常驻）", () => _draft.Resources.IdleUnloadMinutes, n => _draft.Resources.IdleUnloadMinutes = n);
        Integer(response, "内存缓存条数（0 关闭）", () => _draft.Response.MemoryCacheEntries, n => _draft.Response.MemoryCacheEntries = n);
        BuildColumns(); BuildModel(); BuildDictionary();
        var display = Tab("翻译窗 / 后台");
        Toggle(display, "启用当前句子翻译（读取光标所在句）", _draft.TextTranslation.SentenceEnabled, b => _draft.TextTranslation.SentenceEnabled = b);
        Toggle(display, "启用选中文字翻译（读取当前活动文本选区）", _draft.TextTranslation.SelectionEnabled, b => _draft.TextTranslation.SelectionEnabled = b);
        TextField(display, "正文翻译本地来源 ID", _draft.TextTranslation.SourceId, s => _draft.TextTranslation.SourceId = s);
        TextField(display, "正文翻译目标语言", _draft.TextTranslation.TargetLanguage, s => _draft.TextTranslation.TargetLanguage = s);
        Integer(display, "正文稳定等待（ms）", () => _draft.TextTranslation.StableMs, n => _draft.TextTranslation.StableMs = n);
        Integer(display, "正文最大合并等待（ms）", () => _draft.TextTranslation.MaxWaitMs, n => _draft.TextTranslation.MaxWaitMs = n);
        Integer(display, "正文翻译期限（ms）", () => _draft.TextTranslation.DeadlineMs, n => _draft.TextTranslation.DeadlineMs = n);
        display.Children.Add(Button("恢复正文框到屏幕右下角", () => { _draft.TextTranslation.Right = _draft.TextTranslation.Bottom = null; _draft.TextTranslation.Width = 420; _draft.TextTranslation.Height = 180; _draftGeometryRevision = _runtime.GeometryRevision; }));
        display.Children.Add(Note("正文框固定位置，可拖动和缩放。选中文字优先于光标所在句；没有活动目标时为空闲圆圈，确认目标但无法读取时显示原因。正文只交给本地模型，不进入词典缓存或默认日志。两个正文开关默认关闭。"));
        var follow = Toggle(display, "跟随 Windows 应用主题", _draft.Display.FollowSystemTheme, b => _draft.Display.FollowSystemTheme = b);
        var manualDark = Toggle(display, "深色翻译窗（手动）", _draft.Display.Dark, b => _draft.Display.Dark = b);
        manualDark.IsEnabled = !_draft.Display.FollowSystemTheme;
        follow.Checked += (_, _) => manualDark.IsEnabled = false;
        follow.Unchecked += (_, _) => manualDark.IsEnabled = true;
        Toggle(display, "关闭设置窗口后留在托盘", _draft.Startup.CloseToTray, b => _draft.Startup.CloseToTray = b);
        Toggle(display, "登录启动（当前用户）", _draft.Startup.RunAtLogin, b => _draft.Startup.RunAtLogin = b);
        Toggle(display, "启动时预加载模型", _draft.Startup.PreloadModel, b => _draft.Startup.PreloadModel = b);
        Integer(display, "横向偏移（DIP）", () => _draft.Display.OffsetX, n => _draft.Display.OffsetX = n);
        Integer(display, "垂直间隔（DIP）", () => _draft.Display.OffsetY, n => _draft.Display.OffsetY = n);
        display.Children.Add(Note("翻译窗不夺焦点且点击穿透；默认跟随 Windows 应用深浅色，可关闭跟随后手动选择。列颜色对比度不足时使用可读文字颜色；高对比度使用系统颜色。候选更新时保留上一页，释义就绪后整页替换。宽度最多为工作区的 80%。"));
        BuildAdvanced();
    }
    private void BuildColumns()
    {
        var panel = Tab("语言 / 提示词");
        panel.Children.Add(Note("每列代表一个来源 + 一种目标语言。同一来源可重复添加不同语言；同一语言也可并排对比不同来源。颜色为 #RRGGBB。"));
        var rows = new ObservableCollection<ColumnSettings>(_draft.Columns); _draft.Columns = rows.ToList();
        _columns = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = false, Height = 210, SelectionMode = DataGridSelectionMode.Single };
        _columns.Columns.Add(new DataGridCheckBoxColumn { Header = "启用", Binding = new Binding("Enabled") });
        foreach (var pair in new[] { ("ID", "Id"), ("来源 ID", "SourceId"), ("语言代码", "TargetLanguage"), ("浅色", "ColorLight"), ("深色", "ColorDark"), ("宽度", "Width") })
            _columns.Columns.Add(new DataGridTextColumn { Header = pair.Item1, Binding = new Binding(pair.Item2) });
        panel.Children.Add(_columns); var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Button("添加语言列", () => { var source = _draft.Providers.First(); var c = new ColumnSettings { Id = "column-" + Guid.NewGuid().ToString("N")[..6], SourceId = source.Id, TargetLanguage = source.SupportedTargets.First() }; rows.Add(c); _columns.SelectedItem = c; }));
        buttons.Children.Add(Button("删除列", () => { if (_columns.SelectedItem is ColumnSettings c) rows.Remove(c); }));
        buttons.Children.Add(Button("上移", () => { if (_columns.SelectedItem is ColumnSettings c && rows.IndexOf(c) > 0) rows.Move(rows.IndexOf(c), rows.IndexOf(c) - 1); }));
        buttons.Children.Add(Button("下移", () => { if (_columns.SelectedItem is ColumnSettings c && rows.IndexOf(c) < rows.Count - 1) rows.Move(rows.IndexOf(c), rows.IndexOf(c) + 1); })); panel.Children.Add(buttons);
        var fallback = TextField(panel, "选中列的同语言兜底词典 ID（逗号分隔）", "", text => { if (_selectedColumn != null) _selectedColumn.FallbackSourceIds = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(); });
        var prompt = TextField(panel, "选中列的提示词覆盖（留空使用来源默认提示词）", "", text => { if (_selectedColumn != null) _selectedColumn.PromptOverride = string.IsNullOrWhiteSpace(text) ? null : text; }, true);
        panel.Children.Add(Note("变量：{{candidate}}、{{source_language_name}}、{{target_language_name}}。语言代码例：en 英文、ja 日文、ko 韩文、fr 法文；必须被所选来源和兜底词典支持。"));
        _columns.SelectionChanged += (_, _) => { _selectedColumn = null; var selected = _columns.SelectedItem as ColumnSettings; fallback.Text = selected == null ? "" : string.Join(',', selected.FallbackSourceIds); prompt.Text = selected?.PromptOverride ?? ""; _selectedColumn = selected; };
        _columns.SelectedIndex = 0;
        var word = new TextBox { Text = "苹果", MaxWidth = 260, HorizontalAlignment = HorizontalAlignment.Left }; panel.Children.Add(word);
        var output = Note(""); panel.Children.Add(Button("测试已保存来源 / 当前列提示词", async () =>
        {
            if (_selectedColumn == null) return;
            var result = await _runtime.TestPromptAsync(_selectedColumn, word.Text);
            output.Text = $"{result.Status}: {result.Gloss ?? result.ErrorCode ?? "无结果"}";
        })); panel.Children.Add(output);
        _writeFields.Add(() => _draft.Columns = rows.ToList());
    }
    private void BuildModel()
    {
        var panel = Tab("离线模型"); var model = _draft.Providers.FirstOrDefault(p => p.Kind == ProviderKind.LocalModel);
        if (model == null) { panel.Children.Add(Note("未配置本地模型，可在高级配置中添加。")); return; }
        panel.Children.Add(Note($"当前快捷编辑来源：{model.Id}。首次可运行程序目录的 Setup-LLM.cmd，默认下载到同级 LLM；完成后重启程序自动识别。脚本支持自定义目录。已有配置可在此修改路径。"));
        Toggle(panel, "启用该模型来源", model.Enabled, b => model.Enabled = b);
        Toggle(panel, "由外挂托管 llama-server 子进程", model.Managed, b => model.Managed = b);
        TextField(panel, "CPU 备用 server（留空复用主 server）", model.CpuExecutablePath, s => { model.CpuExecutablePath = s; model.UseInstalledModel = false; });
        Toggle(panel, "优先 GPU，初始化失败回退 CPU（关闭则只用 CPU）", model.UseGpu, b => { model.UseGpu = b; model.UseInstalledModel = false; });
        var exe = TextField(panel, "llama-server.exe 绝对路径", model.ExecutablePath, text => { model.ExecutablePath = text; model.UseInstalledModel = false; });
        panel.Children.Add(Button("选择 llama-server", () => { var dialog = new OpenFileDialog { Filter = "Executable|*.exe" }; if (dialog.ShowDialog() == true) exe.Text = dialog.FileName; }));
        var gguf = TextField(panel, "LMT GGUF 路径", model.ModelPath, text => { model.ModelPath = text; model.UseInstalledModel = false; });
        panel.Children.Add(Button("选择 GGUF", () => { var dialog = new OpenFileDialog { Filter = "GGUF|*.gguf" }; if (dialog.ShowDialog() == true) gguf.Text = dialog.FileName; }));
        TextField(panel, "本机服务地址", model.Endpoint, text => model.Endpoint = text);
        TextField(panel, "模型来源 / revision", model.ModelRevision, text => { model.ModelRevision = text; model.UseInstalledModel = false; });
        panel.Children.Add(Note("模型量化（必须与文件一致；切换下载请重新运行 Setup-LLM.cmd）"));
        var quantization = new ComboBox { ItemsSource = new[] { "Q8_0", "Q6_K", "Q5_K_M", "Q4_K_M" }, SelectedItem = model.Quantization };
        quantization.SelectionChanged += (_, _) => { model.Quantization = (string)quantization.SelectedItem; model.Name = "LMT " + model.Quantization; model.UseInstalledModel = false; };
        panel.Children.Add(quantization);
        TextField(panel, "GGUF SHA256（可选，但建议校验）", model.ModelSha256, text => { model.ModelSha256 = text; model.UseInstalledModel = false; });
        Integer(panel, "总上下文 tokens", () => model.ContextTokens, n => model.ContextTokens = n);
        Integer(panel, "输入 token 预算", () => model.InputTokenBudget, n => model.InputTokenBudget = n);
        Integer(panel, "最大输出 tokens（1–64）", () => model.MaxOutputTokens, n => model.MaxOutputTokens = n);
        TextField(panel, "来源默认提示词", model.Prompt, text => model.Prompt = text, true);
        panel.Children.Add(Button("加载已保存模型", _runtime.LoadModelsAsync)); panel.Children.Add(Button("释放模型", _runtime.StopModelsAsync));
    }
    private void BuildDictionary()
    {
        var panel = Tab("本地词典");
        panel.Children.Add(Note("UTF-8 TSV：词头、目标语言代码、释义（Tab 分隔），可选词性和 | 分隔别名。必须填写许可与出处。成功后创建新的 SQLite 包，可在语言列中选用。"));
        var name = new TextBox { Text = "我的词典" }; var id = new TextBox { Text = "dictionary-custom" }; var license = new TextBox { Text = "" }; var source = new TextBox { Text = "" };
        foreach (var pair in new[] { ("词典名称", name), ("来源 ID", id), ("许可证 / SPDX", license), ("数据出处", source) }) { panel.Children.Add(new TextBlock { Text = pair.Item1 }); panel.Children.Add(pair.Item2); }
        panel.Children.Add(Button("选择 TSV 并导入", async () =>
        {
            if (id.Text.Length == 0 || !id.Text.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) throw new ArgumentException("来源 ID 无效");
            var dialog = new OpenFileDialog { Filter = "TSV|*.tsv|Text|*.txt" }; if (dialog.ShowDialog() != true) return;
            var path = Path.Combine(_runtime.DataDirectory, "dictionaries", id.Text + "-" + Guid.NewGuid().ToString("N")[..8] + ".db");
            var manifest = await DictionaryStore.ImportAsync(dialog.FileName, path, name.Text, license.Text, source.Text, default);
            _draft.Providers.RemoveAll(p => p.Id == id.Text);
            _draft.Providers.Add(new() { Id = id.Text, Name = name.Text, Kind = ProviderKind.LocalDictionary, DictionaryPath = path, SupportedTargets = manifest.SupportedTargets.ToList() });
            _status.Text = $"已导入 {manifest.Entries} 条，语言 {string.Join(',', manifest.SupportedTargets)}；保存设置后生效。";
        }));
        foreach (var p in _draft.Providers.Where(p => p.Kind == ProviderKind.LocalDictionary)) panel.Children.Add(Note($"{p.Id} · {p.Name} · {string.Join(',', p.SupportedTargets)}\n{p.DictionaryPath}"));
    }
    private void BuildAdvanced()
    {
        var panel = Tab("高级配置");
        panel.Children.Add(Note("此处可增加不同本地词典、本机模型或 HTTPS 在线兼容模型。在线来源仅在你将 kind 设为 OnlineModel 并显式 enabled=true 时发送可见候选；不会自动联网兜底。密钥只填写环境变量名称，不保存密钥。JSON 编辑后先点击“校验并载入表单”，再保存。"));
        var json = new TextBox { Text = JsonSerializer.Serialize(_draft, SettingsJson.Options), AcceptsReturn = true, AcceptsTab = true, Height = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new("Consolas") }; panel.Children.Add(json);
        panel.Children.Add(Button("用当前表单刷新 JSON", () => { ReadFields(); json.Text = JsonSerializer.Serialize(_draft, SettingsJson.Options); }));
        panel.Children.Add(Button("校验 JSON 并载入表单", () => { var next = JsonSerializer.Deserialize<AppSettings>(json.Text, SettingsJson.Options) ?? throw new ArgumentException("配置为空"); SettingsValidator.ThrowIfInvalid(next); _draft = next; BuildTabs(); }));
        panel.Children.Add(Button("导入配置", () => { var dialog = new OpenFileDialog { Filter = "JSON|*.json" }; if (dialog.ShowDialog() == true) { _draft = SettingsJson.Load(dialog.FileName); BuildTabs(); } }));
        panel.Children.Add(Button("导出配置（仅凭据引用）", () => { ReadFields(); var dialog = new SaveFileDialog { Filter = "JSON|*.json", FileName = "pinyin-gloss-settings.json" }; if (dialog.ShowDialog() == true) SettingsJson.Save(dialog.FileName, _draft); }));
        panel.Children.Add(Note("OCR、应用白名单、磁盘缓存和外部适配器进程是后续扩展，首版尚未启用。完整接口和待验证范围见 docs/architecture.md。"));
    }
    private void ReadFields() { _columns?.CommitEdit(DataGridEditingUnit.Cell, true); _columns?.CommitEdit(DataGridEditingUnit.Row, true); foreach (var write in _writeFields) write(); }
    private async Task SaveAsync()
    {
        if (_saving) return; _saving = true;
        try { ReadFields(); await _runtime.SaveAsync(SettingsJson.Clone(_draft), _draftGeometryRevision); _draft = SettingsJson.Clone(_runtime.Settings); _draftGeometryRevision = _runtime.GeometryRevision; BuildTabs(); }
        finally { _saving = false; }
    }
}
