using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using PinyinGloss.Core;

namespace PinyinGloss.Desktop;
internal sealed class OverlayWindow : Window
{
    private readonly Grid _grid = new(); private readonly Border _border;
    private readonly Dictionary<(string, string), TextBlock> _cells = [];
    private CandidateSnapshot? _snapshot; private AppSettings? _settings;
    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; SizeToContent = SizeToContent.Height; Focusable = false;
        _border = new Border { Padding = new(10), CornerRadius = new(8), BorderThickness = new(1), BorderBrush = Brushes.Gray, Child = _grid };
        Content = _border;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(handle, -20, GetWindowLongPtr(handle, -20) | 0x08000000 | 0x80 | 0x20);
            HwndSource.FromHwnd(handle).AddHook(Hook);
        };
        SizeChanged += (_, _) => Position();
    }
    public void SetSnapshot(CandidateSnapshot snapshot, AppSettings settings)
    {
        _snapshot = snapshot; _settings = settings;
        _border.Background = settings.Display.Dark ? new SolidColorBrush(Color.FromRgb(31, 35, 43)) : Brushes.White;
        _grid.Children.Clear(); _grid.RowDefinitions.Clear(); _grid.ColumnDefinitions.Clear(); _cells.Clear();
        var columns = settings.Columns.Where(c => c.Enabled).ToArray();
        _grid.ColumnDefinitions.Add(new() { Width = new GridLength(125) });
        foreach (var col in columns) _grid.ColumnDefinitions.Add(new() { Width = new GridLength(col.Width, GridUnitType.Star) });
        var foreground = settings.Display.Dark ? Brushes.White : Brushes.Black;
        TextBlock Add(string text, int row, int column, Brush? color = null)
        {
            var block = new TextBlock { Text = text, Foreground = color ?? foreground, Margin = new(6, 5, 6, 5), TextWrapping = TextWrapping.Wrap, MaxHeight = 70, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetRow(block, row); Grid.SetColumn(block, column); _grid.Children.Add(block); return block;
        }
        _grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); Add(snapshot.CaptureMethod == "Demo" ? "模拟候选" : "微软候选", 0, 0);
        for (var i = 0; i < columns.Length; i++)
        {
            var source = settings.Providers.First(p => p.Id == columns[i].SourceId);
            Add($"{source.Name}\n{LanguageCatalog.Name(columns[i].TargetLanguage)}", 0, i + 1, ColorBrush(columns[i], settings));
        }
        var words = snapshot.Candidates.Take(settings.Capture.MaxCandidates).ToArray();
        for (var row = 0; row < words.Length; row++)
        {
            _grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); Add($"{words[row].VisibleIndex}  {words[row].Text}", row + 1, 0);
            for (var col = 0; col < columns.Length; col++) _cells[(words[row].Id, columns[col].Id)] = Add("等待…", row + 1, col + 1, ColorBrush(columns[col], settings));
        }
        Width = 125 + columns.Sum(c => c.Width) + 22;
        var work = WorkArea(snapshot.Bounds); var dpi = CandidateDpi(snapshot); Width = Math.Min(Width, work.Width * settings.Display.MaxWidthRatio / dpi);
        if (!IsVisible) Show(); UpdateLayout(); Position();
    }
    public void MoveSnapshot(CandidateSnapshot snapshot) { _snapshot = snapshot; Position(); }
    public void UpdateCell(TranslationCell cell)
    {
        if (_snapshot?.SnapshotId != cell.SnapshotId || _snapshot.Generation != cell.Generation || _settings?.SettingsRevision != cell.SettingsRevision) return;
        if (!_cells.TryGetValue((cell.Result.CandidateId, cell.ColumnId), out var block)) return;
        var column = _settings.Columns.FirstOrDefault(c => c.Id == cell.ColumnId);
        if (column?.TargetLanguage != cell.TargetLanguage || !_snapshot.Candidates.Any(c => c.Id == cell.Result.CandidateId && c.Text == cell.Result.OriginalText)) return;
        var source = _settings.Providers.FirstOrDefault(p => p.Id == cell.ActualSourceId);
        block.Text = cell.Result.Status == ResultStatus.Ready ? cell.Result.Gloss + (cell.ActualSourceId != column.SourceId ? $"\n[{source?.Name ?? cell.ActualSourceId}]" : "") : cell.Result.Status switch
        { ResultStatus.NotFound => "无释义", ResultStatus.Timeout => "超时", ResultStatus.InvalidOutput => "输出无效", ResultStatus.Unavailable => "来源未就绪", ResultStatus.Skipped => "已跳过", _ => "等待…" };
    }
    public void Invalidate() { _snapshot = null; Hide(); }
    private static Brush ColorBrush(ColumnSettings c, AppSettings s) => SystemParameters.HighContrast ? SystemColors.WindowTextBrush :
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(s.Display.Dark ? c.ColorDark : c.ColorLight));
    private void Position()
    {
        if (_snapshot == null || _settings == null || !IsVisible) return;
        var bounds = _snapshot.Bounds; var work = WorkArea(bounds); var dpi = CandidateDpi(_snapshot);
        var width = ActualWidth * dpi; var height = ActualHeight * dpi;
        var x = Math.Clamp(bounds.X + _settings.Display.OffsetX * dpi, work.X, Math.Max(work.X, work.X + work.Width - width));
        var y = bounds.Y + bounds.Height + _settings.Display.OffsetY * dpi;
        if (y + height > work.Y + work.Height) y = bounds.Y - height - _settings.Display.OffsetY * dpi;
        if (y < work.Y) { Hide(); return; } // Never cover the original candidate popup if there is no room.
        SetWindowPos(new WindowInteropHelper(this).Handle, -1, (int)x, (int)y, (int)width, (int)height, 0x10);
    }
    private static double CandidateDpi(CandidateSnapshot snapshot)
    {
        var parts = snapshot.WindowIdentity.Split(':');
        if (parts.Length > 1 && long.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        { var dpi = GetDpiForWindow((nint)value); if (dpi > 0) return dpi / 96d; }
        return 1;
    }
    private static ScreenRect WorkArea(ScreenRect bounds)
    {
        var rect = new NativeRect { Left = (int)bounds.X, Top = (int)bounds.Y, Right = (int)(bounds.X + bounds.Width), Bottom = (int)(bounds.Y + bounds.Height) };
        var monitor = MonitorFromRect(ref rect, 2); var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(monitor, ref info)) return new(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
        return new(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    { if (message == 0x21) { handled = true; return 3; } if (message == 0x84) { handled = true; return -1; } return 0; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint MonitorFromRect(ref NativeRect rect, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
