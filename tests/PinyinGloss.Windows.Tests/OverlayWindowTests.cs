using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using PinyinGloss.Core;
using PinyinGloss.Desktop;

namespace PinyinGloss.Windows.Tests;

[Trait("Category", "InteractiveDesktop")]
public class OverlayWindowTests
{
    [Fact]
    public Task Candidate_numbers_and_glosses_remain_matched_when_stale_results_arrive() => OnSta(() =>
    {
        var work = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var overlay = new OverlayWindow();
        try
        {
            var snapshot = Snapshot(new(work.X + 50, work.Y + 50, 300, 32));
            overlay.SetSnapshot(snapshot, AppSettings.CreateDefault());
            var cell = new TranslationCell("same-page", 1, 1, "lmt-en", "en", "dictionary", "v1",
                new("fresh", "保鲜", "en", ResultStatus.Ready, "keep fresh"));
            overlay.UpdateCell(cell);
            overlay.UpdateCell(cell with { Generation = 0, Result = cell.Result with { Gloss = "stale" } });
            overlay.UpdateCell(cell with { Result = cell.Result with { OriginalText = "保险", Gloss = "wrong word" } });
            overlay.UpdateCell(cell with { TargetLanguage = "ja", Result = cell.Result with { Gloss = "wrong language" } });
            var grid = (System.Windows.Controls.Grid)((System.Windows.Controls.Border)overlay.Content).Child;
            string At(int row, int column) => grid.Children.OfType<System.Windows.Controls.TextBlock>()
                .Single(b => System.Windows.Controls.Grid.GetRow(b) == row && System.Windows.Controls.Grid.GetColumn(b) == column).Text;
            Assert.Equal("1  保险", At(1, 0));
            Assert.Equal("2  保鲜", At(2, 0));
            Assert.Equal("3  包线", At(3, 0));
            Assert.Equal("keep fresh\n[示例词典]", At(2, 1));
            Assert.Equal("等待…", At(1, 1));
            Assert.Equal("等待…", At(3, 1));
        }
        finally { overlay.Close(); }
    });

    [Fact]
    public Task Moving_candidate_to_available_space_restores_hidden_overlay() => OnSta(() =>
    {
        var work = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var overlay = new OverlayWindow();
        try
        {
            var snapshot = Snapshot(new(work.X, work.Y, work.Width, work.Height));
            overlay.SetSnapshot(snapshot, AppSettings.CreateDefault());
            Assert.False(overlay.IsVisible);

            overlay.MoveSnapshot(snapshot with { Bounds = new(work.X + 50, work.Y + 50, 300, 32) });
            Assert.True(overlay.IsVisible);
        }
        finally { overlay.Close(); }
    });

    [Fact]
    public Task Showing_and_restoring_overlay_preserves_foreground_and_input_styles() => OnSta(() =>
    {
        var work = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var foreground = GetForegroundWindow();
        var overlay = new OverlayWindow();
        try
        {
            var snapshot = Snapshot(new(work.X + 50, work.Y + 50, 300, 32));
            overlay.SetSnapshot(snapshot, AppSettings.CreateDefault());
            Assert.True(overlay.IsVisible);
            Assert.Equal(foreground, GetForegroundWindow());
            var style = GetWindowLongPtr(new WindowInteropHelper(overlay).Handle, -20).ToInt64();
            Assert.Equal(0x08000020L, style & 0x08000020L);

            overlay.MoveSnapshot(snapshot with { Bounds = new(work.X, work.Y, work.Width, work.Height) });
            Assert.False(overlay.IsVisible);
            overlay.MoveSnapshot(snapshot);
            Assert.True(overlay.IsVisible);
            Assert.Equal(foreground, GetForegroundWindow());

            overlay.Invalidate();
            Assert.False(overlay.IsVisible);
        }
        finally { overlay.Close(); }
    });

    private static CandidateSnapshot Snapshot(ScreenRect bounds) => new("windows-test", "same-page", 1, "test", bounds,
        [new("insurance", "保险", 1), new("fresh", "保鲜", 2), new("wire", "包线", 3)]);

    private static Task OnSta(Action test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { test(); completion.TrySetResult(); }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
}
