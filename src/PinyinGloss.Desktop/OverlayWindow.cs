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
    private readonly OverlayContent _content = new(); private readonly Border _border;
    private CandidateSnapshot? _snapshot; private AppSettings? _settings;
    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        OverlayTheme.ApplyTypography(this);
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; SizeToContent = SizeToContent.Height; Focusable = false;
        _border = new Border { Padding = new(10), CornerRadius = new(OverlayTheme.CornerRadius), BorderThickness = new(1) };
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
        var replaced = _content.SetSnapshot(snapshot, settings);
        ApplyTheme();
        if (replaced) PresentPage(); else Position();
    }
    public void BeginUpdate() => _content.BeginUpdate();
    public void MoveSnapshot(CandidateSnapshot snapshot) { _snapshot = snapshot; Position(); }
    public void UpdateCell(TranslationCell cell)
    {
        if (_content.UpdateCell(cell) && !ReferenceEquals(_border.Child, _content.Displayed)) PresentPage();
    }
    public void Invalidate() { _snapshot = null; _content.Invalidate(); Hide(); }
    private void PresentPage()
    {
        if (_snapshot == null || _settings == null) return;
        _border.Child = _content.Displayed;
        Width = 125 + _settings.Columns.Where(c => c.Enabled).Sum(c => c.Width) + 22;
        var work = WorkArea(_snapshot.Bounds); var dpi = CandidateDpi(_snapshot);
        Width = Math.Min(Width, work.Width * _settings.Display.MaxWidthRatio / dpi);
        if (!IsVisible) Show(); UpdateLayout(); Position();
    }
    private void ApplyTheme()
    {
        if (_settings == null) return;
        var theme = OverlayTheme.Create(_settings.Display, OverlayTheme.ReadSystemDark(), SystemParameters.HighContrast);
        _border.Background = new SolidColorBrush(theme.Background); _border.BorderBrush = new SolidColorBrush(theme.Border);
        _content.ApplyTheme(theme);
    }
    private void Position()
    {
        if (_snapshot == null || _settings == null) return;
        var bounds = _snapshot.Bounds; var work = WorkArea(bounds); var dpi = CandidateDpi(_snapshot);
        var width = ActualWidth * dpi; var height = ActualHeight * dpi;
        var x = Math.Clamp(bounds.X + _settings.Display.OffsetX * dpi, work.X, Math.Max(work.X, work.X + work.Width - width));
        var y = bounds.Y + bounds.Height + _settings.Display.OffsetY * dpi;
        if (y + height > work.Y + work.Height) y = bounds.Y - height - _settings.Display.OffsetY * dpi;
        if (y < work.Y) { Hide(); return; } // Never cover the original candidate popup if there is no room.
        if (!IsVisible) Show();
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
    {
        if (message == 0x21) { handled = true; return 3; }
        if (message == 0x84) { handled = true; return -1; }
        // WM_SETTINGCHANGE / WM_THEMECHANGED / WM_SYSCOLORCHANGE: recolor in place after WPF refreshes system colors.
        if (message is 0x1A or 0x31A or 0x15) Dispatcher.BeginInvoke(ApplyTheme);
        return 0;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint MonitorFromRect(ref NativeRect rect, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
