using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PinyinGloss.Core;

// 显式 Windows 现场验收宿主，不进入普通测试/CI；不注入快照，不修改用户配置。
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        if (args.Length < 2 || !int.TryParse(args[1], out var seconds) || seconds is < 1 or > 1800)
        { Console.WriteLine("用法：Windows.Acceptance SETTINGS.json SECONDS [--include-text] [--models]"); return 2; }
        var includeText = args.Contains("--include-text");
        var knownWebText = args.Contains("--known-web-text");
        var allowModels = args.Contains("--models");
        var settingsPath = Path.GetFullPath(args[0]);
        var settings = SettingsJson.Load(settingsPath);
        if (settings.Startup.RunAtLogin || settings.Providers.Any(p => p.Enabled && p.Kind == ProviderKind.OnlineModel) ||
            !allowModels && (settings.Startup.PreloadModel || settings.Providers.Any(p => p.Enabled && p.Kind != ProviderKind.LocalDictionary)))
            throw new InvalidDataException("宿主禁止登录启动/在线来源；本地模型必须显式 --models");
        SettingsValidator.ThrowIfInvalid(settings);
        var clock = Stopwatch.StartNew();
        void Log(string kind, object value) => Console.WriteLine(JsonSerializer.Serialize(new { ms = clock.Elapsed.TotalMilliseconds, kind, value }));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // 只反射当前工程内部运行组件，避免为验收增加产品启动参数或公开 API。
        var runtimeType = typeof(PinyinGloss.Desktop.App).Assembly.GetType("PinyinGloss.Desktop.DesktopRuntime", true)!;
        var runtime = Activator.CreateInstance(runtimeType, settings, settingsPath, Path.GetDirectoryName(settingsPath)!)!;
        object? Field(string name) => runtimeType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime);
        var overlay = (Window)Field("_overlay")!;
        var textOverlay = (Window)Field("_textOverlay")!;
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        string? lastSnapshot = null, lastRender = null; nint lastForeground = -1; bool? lastCandidateVisible = null;
        string? lastTextRender = null;
        string? lastFocus = null, lastModel = null, lastKeyboardFocus = null;
        IEnumerable<string> Texts(DependencyObject root)
        {
            if (root is TextBlock block) yield return block.Text;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var text in Texts(VisualTreeHelper.GetChild(root, i))) yield return text;
        }
        timer.Tick += (_, _) =>
        {
            var current = (CandidateSnapshot?)Field("_current");
            if (lastSnapshot != current?.SnapshotId)
            {
                lastSnapshot = current?.SnapshotId;
                Log("snapshot", current == null ? new { hidden = true } : (object)new { current.SnapshotId, current.Generation, current.WindowIdentity, current.Bounds,
                    candidates = current.Candidates.Select(c => new { c.Id, c.VisibleIndex, text = includeText ? c.Text : null }) });
            }
            var foreground = GetForegroundWindow();
            if (foreground != lastForeground) { lastForeground = foreground; Log("foreground", foreground.ToInt64().ToString("x")); }
            var gui = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
            if (GetGUIThreadInfo(GetWindowThreadProcessId(foreground, out _), ref gui))
            {
                var keyboard = JsonSerializer.Serialize(new { foreground = foreground.ToInt64().ToString("x"), active = gui.Active.ToInt64().ToString("x"), focus = gui.Focus.ToInt64().ToString("x") });
                if (keyboard != lastKeyboardFocus) { lastKeyboardFocus = keyboard; Log("keyboard-focus", JsonSerializer.Deserialize<JsonElement>(keyboard)); }
            }
            var parts = current?.WindowIdentity.Split(':');
            bool? candidateVisible = parts is { Length: > 1 } && long.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hwnd) ? IsWindowVisible((nint)hwnd) : null;
            if (candidateVisible != lastCandidateVisible) { lastCandidateVisible = candidateVisible; Log("candidateHostVisible", candidateVisible!); }
            var render = JsonSerializer.Serialize(new { overlay.IsVisible, overlay.IsActive, foreground = foreground.ToInt64().ToString("x"),
                texts = includeText && overlay.IsVisible ? Texts(overlay).ToArray() : null });
            if (render != lastRender) { lastRender = render; Log("render", JsonSerializer.Deserialize<JsonElement>(render)); }
            var textSnapshot = (TextSnapshot?)Field("_textSnapshot");
            var focus = (InputFocus?)Field("_focus");
            var focusState = JsonSerializer.Serialize(new { focus?.SessionId, focus?.ForegroundWindow, focus?.TargetIdentity, focus?.Editable });
            if (focusState != lastFocus) { lastFocus = focusState; Log("text-focus", JsonSerializer.Deserialize<JsonElement>(focusState)); }
            var modelState = (string)runtimeType.GetProperty("ModelStatus")!.GetValue(runtime)!;
            if (modelState != lastModel) { lastModel = modelState; Log("model-state", modelState); }
            var textRender = JsonSerializer.Serialize(new { textOverlay.IsVisible, textOverlay.IsActive, textOverlay.Left, textOverlay.Top,
                textOverlay.Width, textOverlay.Height, textSnapshot?.SessionId, textSnapshot?.Version, textSnapshot?.Mode, textSnapshot?.Status,
                length = textSnapshot?.Text.Length,
                knownText = !knownWebText ? null : textSnapshot?.Text switch {
                    "测试" => "test", "我喜欢苹果。" => "apple-sentence", "今天的天气很好。" => "weather-sentence",
                    "我喜欢苹果。今天的天气很好。" => "both-sentences", "这是第一句。\n这是第二句。" => "multiline",
                    "欢迎使用本地翻译。" => "editable", "我喜欢阅读中文书籍。" => "readonly", "苹果" => "apple", _ => null },
                original = includeText ? textSnapshot?.Text : null, texts = includeText && textOverlay.IsVisible ? Texts(textOverlay).ToArray() : null });
            if (textRender != lastTextRender) { lastTextRender = textRender; Log("text-render", JsonSerializer.Deserialize<JsonElement>(textRender)); }
        };
        app.Startup += async (_, _) =>
        {
            try
            {
                await (Task)runtimeType.GetMethod("StartAsync")!.Invoke(runtime, null)!;
                var coordinator = (TranslationCoordinator)Field("_coordinator")!;
                coordinator.CellUpdated += cell => app.Dispatcher.BeginInvoke(() => Log("cell", new { cell.SnapshotId, cell.Generation, cell.ColumnId, cell.TargetLanguage, cell.ActualSourceId,
                    cell.Result.CandidateId, cell.Result.Status, text = includeText ? cell.Result.OriginalText : null, gloss = includeText ? cell.Result.Gloss : null }));
                var reader = Field("_reader")!;
                Action<ReaderMessage> observe = message =>
                {
                    if (message.Kind != ReaderMessageKind.Heartbeat && (message.Kind != ReaderMessageKind.Snapshot || message.Snapshot?.SnapshotId != lastSnapshot))
                        app.Dispatcher.BeginInvoke(() => Log("reader", new { message.Kind, message.Status, message.Snapshot?.SnapshotId, message.TextDiagnostic }));
                };
                reader.GetType().GetEvent("Message")!.AddEventHandler(reader, observe);
                Log("ready", new { settingsPath, seconds, includeText, columns = settings.Columns.Where(c => c.Enabled).Select(c => new { c.Id, c.SourceId, c.TargetLanguage }) });
                timer.Start();
                await Task.Delay(TimeSpan.FromSeconds(seconds));
                app.Shutdown();
            }
            catch (Exception error) { Log("error", error.ToString()); app.Shutdown(1); }
        };
        app.Exit += (_, _) => { timer.Stop(); ((IDisposable)runtime).Dispose(); Log("exit", "验收宿主退出"); };
        return app.Run();
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo
    {
        public int Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }
}
