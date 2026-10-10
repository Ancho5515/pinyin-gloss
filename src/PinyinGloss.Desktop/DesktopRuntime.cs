using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using PinyinGloss.Core;
using PinyinGloss.Providers;
using Forms = System.Windows.Forms;

namespace PinyinGloss.Desktop;
internal sealed class DesktopRuntime : IDisposable
{
    private readonly OverlayWindow _overlay = new();
    private ReaderSupervisor? _reader; private TranslationCoordinator? _coordinator;
    private IReadOnlyDictionary<string, ITranslationProvider> _providers = new Dictionary<string, ITranslationProvider>();
    private readonly Forms.NotifyIcon _tray;
    private readonly DispatcherTimer _foregroundTimer; private readonly DispatcherTimer _maintenance;
    private CancellationTokenSource _lifetime = new(); private MainWindow? _main;
    private CandidateSnapshot? _current; private bool _demo, _dictionaryOnly, _disposed, _reconfiguring;
    public AppSettings Settings { get; private set; }
    public string SettingsPath { get; }
    public string DataDirectory { get; }
    public bool ExitRequested { get; private set; }
    public string Status { get; private set; } = "等待微软拼音候选（UIA 尚未现场验证）";
    public event Action<string>? StatusChanged;
    public DesktopRuntime(AppSettings settings, string settingsPath, string dataDirectory)
    {
        Settings = settings; SettingsPath = settingsPath; DataDirectory = dataDirectory;
        _tray = new() { Icon = System.Drawing.SystemIcons.Information, Text = "Pinyin Gloss", Visible = true };
        _tray.DoubleClick += (_, _) => Dispatch(OpenSettings);
        var menu = new Forms.ContextMenuStrip();
        void Item(string title, Action action) => menu.Items.Add(title, null, (_, _) => Dispatch(action));
        Item("打开设置", OpenSettings); Item("暂停 / 恢复", () => { Settings.Enabled = !Settings.Enabled; Invalidate(); SetStatus(Settings.Enabled ? "已恢复" : "已暂停"); _coordinator?.UpdateSettings(EffectiveSettings()); });
        Item("切换临时词典模式", () => { _dictionaryOnly = !_dictionaryOnly; Invalidate(); _coordinator?.UpdateSettings(EffectiveSettings()); SetStatus(_dictionaryOnly ? "临时仅词典" : "已恢复模型模式"); });
        Item("加载模型", () => _ = LoadModelsAsync()); Item("释放模型", () => _ = StopModelsAsync());
        Item("重启候选读取", () => _ = RestartReaderAsync()); Item("退出演示 / 恢复读取", EndDemo);
        Item("退出", Exit); _tray.ContextMenuStrip = menu;
        _foregroundTimer = new(TimeSpan.FromMilliseconds(50), DispatcherPriority.Send, (_, _) => CheckForeground(), Application.Current.Dispatcher);
        _maintenance = new(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => _ = MaintainModelsAsync(), Application.Current.Dispatcher);
    }
    public void Attach(MainWindow main) => _main = main;
    public async Task StartAsync() => await ReconfigureAsync();
    public async Task SaveAsync(AppSettings next)
    {
        if (_reconfiguring) throw new InvalidOperationException("设置正在应用");
        SettingsValidator.ThrowIfInvalid(next);
        next.SettingsRevision = Settings.SettingsRevision + 1;
        SettingsJson.Save(SettingsPath, next);
        ApplyLoginStartup(next.Startup.RunAtLogin);
        Settings = SettingsJson.Clone(next); await ReconfigureAsync(); SetStatus("设置已保存并生效");
    }
    private async Task ReconfigureAsync()
    {
        _reconfiguring = true;
        try
        {
            Invalidate(); _lifetime.Cancel(); _coordinator?.Dispose();
            if (_reader != null) { await _reader.DisposeAsync(); _reader = null; }
            foreach (var p in _providers.Values) { if (p is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync(); else if (p is IDisposable disposable) disposable.Dispose(); }
            _lifetime.Dispose(); _lifetime = new();
            _providers = ProviderFactory.Create(Settings, SetStatus);
            _coordinator = new(_providers, EffectiveSettings());
            _coordinator.CellUpdated += cell => Dispatch(() =>
            {
                if (_current?.SnapshotId != cell.SnapshotId || _current.Generation != cell.Generation || Settings.SettingsRevision != cell.SettingsRevision || !Settings.Enabled) return;
                if (!_providers.TryGetValue(cell.ActualSourceId, out var provider) || provider.Describe().Version != cell.SourceVersion) return;
                CheckForeground(); if (_current == null) return; _overlay.UpdateCell(cell);
            });
            if (!_demo) StartReader();
            if (Settings.Startup.PreloadModel) _ = LoadModelsAsync();
        }
        finally { _reconfiguring = false; }
    }
    private AppSettings EffectiveSettings()
    {
        var settings = SettingsJson.Clone(Settings);
        if (_dictionaryOnly) foreach (var p in settings.Providers.Where(p => p.Kind != ProviderKind.LocalDictionary)) p.Enabled = false;
        return settings;
    }
    private void StartReader()
    {
        var reader = new ReaderSupervisor(); _reader = reader; reader.Message += message => Dispatch(() => { if (ReferenceEquals(_reader, reader)) Receive(message); }); reader.Start(Settings.Capture);
    }
    private void Receive(ReaderMessage message)
    {
        if (_disposed || _demo || _reconfiguring) return;
        if (message.Kind == ReaderMessageKind.Hidden) { Invalidate(); if (message.Status != null) SetStatus(message.Status); }
        else if (message.Kind == ReaderMessageKind.Status) SetStatus(message.Status ?? "当前环境无法读取候选");
        else if (message.Kind == ReaderMessageKind.Snapshot && message.Snapshot != null)
        {
            if (!Settings.Enabled || !ValidSnapshot(message.Snapshot)) { Invalidate(); return; }
            var snapshot = message.Snapshot;
            if (_current?.SnapshotId == snapshot.SnapshotId) { _current = snapshot; _overlay.MoveSnapshot(snapshot); return; }
            _current = snapshot; _overlay.SetSnapshot(snapshot, Settings); _coordinator?.Submit(snapshot); SetStatus("已读取可见候选，翻译中");
        }
    }
    private static bool ValidSnapshot(CandidateSnapshot snapshot) => snapshot.Candidates.Count is > 0 and <= 10 &&
        snapshot.Candidates.All(c => !string.IsNullOrWhiteSpace(c.Id) && c.Text.Length is > 0 and <= 64 && c.VisibleIndex is >= 1 and <= 10) &&
        snapshot.Candidates.Select(c => c.Id).Distinct().Count() == snapshot.Candidates.Count &&
        double.IsFinite(snapshot.Bounds.X) && double.IsFinite(snapshot.Bounds.Y) && snapshot.Bounds.Width > 0 && snapshot.Bounds.Height > 0;
    private void CheckForeground()
    {
        if (_demo || _current == null) return;
        var part = _current.WindowIdentity.Split(':')[0];
        var parts = _current.WindowIdentity.Split(':');
        if (!long.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var handle) || (nint)handle != GetForegroundWindow() ||
            parts.Length < 2 || !long.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var candidateWindow) || !IsWindowVisible((nint)candidateWindow)) Invalidate();
    }
    private void Invalidate() { _current = null; _coordinator?.Hide(); _overlay.Invalidate(); }
    public void OpenSettings() { _main?.Show(); if (_main != null) { _main.WindowState = WindowState.Normal; _main.Activate(); } }
    public void ShowDemo()
    {
        _demo = true; Invalidate();
        var snapshot = new CandidateSnapshot("demo", Guid.NewGuid().ToString("N"), 1, "demo", new(120, 180, 350, 32),
            new[] { new Candidate("apple", "苹果", 1), new Candidate("insurance", "保险", 2), new Candidate("support", "支持", 3) }, CaptureMethod: "Demo");
        _current = snapshot; _overlay.SetSnapshot(snapshot, Settings); _coordinator?.Submit(snapshot);
        SetStatus("模拟候选演示：并非微软输入法采集结果");
    }
    public void EndDemo() { _demo = false; Invalidate(); SetStatus("已恢复微软候选观察"); if (_reader == null) StartReader(); }
    public async Task RestartReaderAsync()
    {
        Invalidate(); if (_reader != null) await _reader.DisposeAsync(); _reader = null;
        _demo = false; StartReader(); SetStatus("已重启候选读取");
    }
    public async Task LoadModelsAsync()
    {
        var providers = _providers.Values.Where(p => p.Describe().Kind == ProviderKind.LocalModel).ToArray();
        SetStatus("正在加载离线模型，词典继续可用");
        foreach (var p in providers) { try { await p.StartAsync(_lifetime.Token); } catch (OperationCanceledException) { return; } }
        SetStatus(string.Join("；", providers.OfType<HttpModelProvider>().Select(p => p.State)) is { Length: > 0 } text ? text : "未启用模型来源");
    }
    public async Task StopModelsAsync()
    {
        Invalidate();
        foreach (var p in _providers.Values.Where(p => p.Describe().Kind == ProviderKind.LocalModel).ToArray()) await p.StopAsync(_lifetime.Token);
        SetStatus("模型已释放");
    }
    private async Task MaintainModelsAsync()
    {
        if (_disposed || _reconfiguring || Settings.Resources.IdleUnloadMinutes == 0) return;
        foreach (var p in _providers.Values.OfType<HttpModelProvider>().ToArray())
            if (p.Describe().Kind == ProviderKind.LocalModel && DateTimeOffset.UtcNow - p.LastUsed > TimeSpan.FromMinutes(Settings.Resources.IdleUnloadMinutes))
                try { await p.StopAsync(_lifetime.Token); } catch (OperationCanceledException) { }
    }
    public async Task<ProviderResult> TestPromptAsync(ColumnSettings column, string word)
    {
        if (!_providers.TryGetValue(column.SourceId, out var provider)) throw new InvalidOperationException("来源未启用或加载失败");
        Invalidate();
        if (_coordinator == null) throw new InvalidOperationException("调度器尚未就绪");
        return await _coordinator.QueryManualAsync(column, word, _lifetime.Token);
    }
    public void RunDiagnostic()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "reader", "PinyinGloss.CandidateReader.exe");
        if (!File.Exists(exe)) { SetStatus("诊断程序不存在，请使用完整发布包"); return; }
        Process.Start(new ProcessStartInfo(exe, "--diagnose --seconds 60") { UseShellExecute = true });
    }
    public void SetStatus(string status) { if (_disposed) return; Status = status; Dispatch(() => StatusChanged?.Invoke(status)); }
    private static void Dispatch(Action action) { if (!Application.Current.Dispatcher.HasShutdownStarted) Application.Current.Dispatcher.BeginInvoke(action); }
    private static void ApplyLoginStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("PinyinGloss", $"\"{Environment.ProcessPath}\" --background"); else key.DeleteValue("PinyinGloss", false);
    }
    public void Exit() { ExitRequested = true; _main?.Close(); Application.Current.Shutdown(); }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _foregroundTimer.Stop(); _maintenance.Stop(); Invalidate(); _lifetime.Cancel();
        _coordinator?.Dispose(); if (_reader != null) _ = _reader.DisposeAsync();
        foreach (var p in _providers.Values) if (p is IDisposable d) d.Dispose();
        _tray.Visible = false; _tray.Dispose(); _overlay.Close();
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
}
