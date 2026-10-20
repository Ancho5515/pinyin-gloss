using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using PinyinGloss.Core;

namespace PinyinGloss.Desktop;

internal sealed class TextOverlayWindow : Window
{
    private readonly Border _border = new() { BorderThickness = new(1) };
    private readonly TextBlock _title = new() { Margin = new(10, 5, 10, 5), FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _original = new() { Margin = new(10, 6, 10, 4), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _translation = new() { Margin = new(10, 4, 10, 8), TextWrapping = TextWrapping.Wrap };
    private readonly DockPanel _panel = new();
    private AppSettings _settings = AppSettings.CreateDefault();
    private TextSnapshot? _snapshot;
    private string? _shownOriginal, _shownTranslation;
    private bool _collapsed = true, _placing;
    public event Action<double, double, double, double>? GeometryChanged;
    public TextOverlayWindow()
    {
        Title = "Pinyin Gloss · 正文翻译";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowActivated = false; Focusable = false; ShowInTaskbar = false; Topmost = true;
        OverlayTheme.ApplyTypography(this); Content = _border;
        DockPanel.SetDock(_title, Dock.Top); _panel.Children.Add(_title);
        var body = new StackPanel(); body.Children.Add(_original); body.Children.Add(_translation);
        _panel.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(handle, -20, GetWindowLongPtr(handle, -20) | 0x08000000 | 0x80);
            HwndSource.FromHwnd(handle).AddHook(Hook);
        };
        Configure(_settings);
    }
    public void Configure(AppSettings settings) { _settings = SettingsJson.Clone(settings); Collapse(); ApplyTheme(); }
    public void ShowConfigured() { if (!_settings.Enabled || !_settings.TextTranslation.Enabled) { Hide(); return; } if (!IsVisible) Show(); Place(); }
    public void Collapse()
    {
        _snapshot = null; _shownOriginal = _shownTranslation = null; _original.Text = _translation.Text = _title.Text = "";
        _collapsed = true; _placing = true; MinWidth = MinHeight = 0; Width = Height = 28; ResizeMode = ResizeMode.NoResize;
        _border.Child = null; _border.CornerRadius = new(14); _placing = false;
        if (IsVisible) Place();
    }
    public void Present(TextSnapshot snapshot, ProviderResult? result)
    {
        var same = _snapshot?.SessionId == snapshot.SessionId && _snapshot.TargetIdentity == snapshot.TargetIdentity && _snapshot.Mode == snapshot.Mode;
        if (!same) { _shownOriginal = _shownTranslation = null; }
        _snapshot = snapshot;
        if (_collapsed)
        {
            _collapsed = false; _placing = true; MinWidth = 240; MinHeight = 100;
            Width = _settings.TextTranslation.Width; Height = _settings.TextTranslation.Height; ResizeMode = ResizeMode.CanResize;
            _border.Child = _panel; _border.CornerRadius = new(OverlayTheme.CornerRadius); _placing = false;
        }
        _title.Text = (snapshot.Mode == TextMode.Selection ? "选中文本" : "当前句子") + " · " + LanguageCatalog.Name(_settings.TextTranslation.TargetLanguage);
        if (result != null) Render(result);
        else if (snapshot.Status == "composing" && _shownTranslation != null)
        { _original.Text = _shownOriginal; _translation.Text = _shownTranslation + "\n输入中…"; }
        else if (snapshot.Status != null) { _shownOriginal = _shownTranslation = null; _original.Text = snapshot.Text; _translation.Text = Error(snapshot.Status); }
        else if (string.IsNullOrWhiteSpace(snapshot.Text)) { _shownOriginal = _shownTranslation = null; _original.Text = ""; _translation.Text = "等待输入"; }
        else if (_shownTranslation != null) { _original.Text = _shownOriginal; _translation.Text = _shownTranslation + "\n更新中…"; }
        else { _original.Text = snapshot.Text; _translation.Text = "翻译中…"; }
        ApplyTheme(); if (IsVisible) Place();
    }
    public void UpdateResult(TextTranslation result)
    {
        if (_snapshot != result.Snapshot || _settings.SettingsRevision != result.SettingsRevision || result.Result.OriginalText != _snapshot.Text ||
            result.Result.TargetLanguage != _settings.TextTranslation.TargetLanguage) return;
        Render(result.Result);
    }
    public void PresentReadStatus(TextReadDiagnostic diagnostic)
    {
        if (diagnostic.Status is TextReadStatus.NoFocusedTarget or TextReadStatus.NoSelection or TextReadStatus.PasswordTarget or TextReadStatus.TargetChanged)
        { Collapse(); _border.ToolTip = diagnostic.Describe(); return; }
        if (diagnostic.Status == TextReadStatus.Success)
        {
            if (_snapshot is { } snapshot && snapshot.SessionId == diagnostic.SessionId && snapshot.Version == diagnostic.Version && _shownTranslation != null)
            { _original.Text = _shownOriginal; _translation.Text = _shownTranslation; }
            return;
        }
        if (diagnostic.Status == TextReadStatus.Composing) return;
        if (diagnostic.Status == TextReadStatus.Reading && _snapshot != null) { _translation.Text = "正在读取…"; return; }
        Present(new(diagnostic.SessionId ?? "status", diagnostic.Version, diagnostic.ForegroundWindow,
            diagnostic.TargetIdentity ?? "status", TextMode.Sentence, "", "read_status"), null);
        _title.Text = "正文翻译"; _translation.Text = diagnostic.Describe();
    }
    private void Render(ProviderResult result)
    {
        _original.Text = _snapshot?.Text ?? "";
        _title.Text = (_snapshot?.Mode == TextMode.Selection ? "选中文本" : "当前句子") + " · " + LanguageCatalog.Name(_settings.TextTranslation.TargetLanguage) +
            (result.Status == ResultStatus.Ready ? " · 翻译成功" : "");
        _translation.Text = result.Status == ResultStatus.Ready ? result.Gloss : result.Status switch
        { ResultStatus.Timeout => "翻译超时", ResultStatus.Cancelled => "已取消", ResultStatus.InvalidOutput => Error(result.ErrorCode),
            ResultStatus.Unavailable when result.ErrorCode == "model_loading" => "模型加载中…", _ => "本地模型未就绪，请检查设置" };
        if (result.Status == ResultStatus.Ready) { _shownOriginal = _original.Text; _shownTranslation = _translation.Text; }
    }
    private static string Error(string? status) => status switch
    { "too_long" => "文本过长", "multiple_selections" => "不支持多个选区", "composing" => "输入中…", "prompt_token_budget" => "文本超出模型输入预算", "output_truncated" => "译文达到输出上限，未完整生成",
        "invalid_text_scope" => TextReadMessages.Describe(TextReadStatus.InvalidTextScope), "no_suitable_provider" => TextReadMessages.Describe(TextReadStatus.NoSuitableProvider),
        "access_denied" => TextReadMessages.Describe(TextReadStatus.AccessDenied), "no_caret" => TextReadMessages.Describe(TextReadStatus.NoCaret), _ => "当前应用不支持正文读取" };
    private void ApplyTheme()
    {
        var theme = OverlayTheme.Create(_settings.Display, OverlayTheme.ReadSystemDark(), SystemParameters.HighContrast);
        _border.Background = new SolidColorBrush(theme.Background); _border.BorderBrush = new SolidColorBrush(theme.Border);
        foreach (var text in new[] { _title, _original, _translation }) text.Foreground = new SolidColorBrush(theme.Foreground);
    }
    internal static Rect FitToWorkArea(Rect work, double width, double height, double? right, double? bottom)
    {
        width = Math.Min(width, work.Width); height = Math.Min(height, work.Height);
        var fittedRight = Math.Clamp(right ?? (work.Right - 16), work.Left + Math.Min(width, work.Width), work.Right);
        var fittedBottom = Math.Clamp(bottom ?? (work.Bottom - 16), work.Top + Math.Min(height, work.Height), work.Bottom);
        return new(fittedRight - width, fittedBottom - height, width, height);
    }
    private void Place()
    {
        if (_placing) return; _placing = true;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var text = _settings.TextTranslation;
            var screen = text.Right.HasValue && text.Bottom.HasValue
                ? System.Windows.Forms.Screen.FromPoint(new((int)(text.Right.Value * dpi), (int)(text.Bottom.Value * dpi)))
                : System.Windows.Forms.Screen.PrimaryScreen!;
            var work = screen.WorkingArea;
            var bounds = FitToWorkArea(new(work.Left / dpi, work.Top / dpi, work.Width / dpi, work.Height / dpi), Width, Height, text.Right, text.Bottom);
            if (!_collapsed) { MinWidth = Math.Min(240, bounds.Width); MinHeight = Math.Min(100, bounds.Height); }
            Width = bounds.Width; Height = bounds.Height; Left = bounds.Left; Top = bounds.Top;
        }
        finally { _placing = false; }
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x21) { handled = true; return 3; }
        if (message == 0x84)
        {
            var point = PointFromScreen(new((short)(lParam.ToInt64() & 0xffff), (short)((lParam.ToInt64() >> 16) & 0xffff)));
            handled = true;
            if (_collapsed) return 2;
            var left = point.X < 6; var right = point.X > ActualWidth - 6; var top = point.Y < 6; var bottom = point.Y > ActualHeight - 6;
            if (top) return left ? 13 : right ? 14 : 12;
            if (bottom) return left ? 16 : right ? 17 : 15;
            if (left) return 10; if (right) return 11;
            return point.Y < 30 ? 2 : 1;
        }
        if (message == 0x232 && !_placing)
        {
            var text = _settings.TextTranslation;
            text.Right = Left + Width; text.Bottom = Top + Height;
            Place();
            text.Right = Left + Width; text.Bottom = Top + Height;
            if (!_collapsed) { text.Width = Math.Clamp(Width, 240, 1600); text.Height = Math.Clamp(Height, 100, 1200); }
            GeometryChanged?.Invoke(text.Right.Value, text.Bottom.Value, text.Width, text.Height);
        }
        if (message is 0x1a or 0x31a or 0x15) Dispatcher.BeginInvoke(ApplyTheme);
        if (message == 0x2e0) Dispatcher.BeginInvoke(Place);
        return 0;
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
