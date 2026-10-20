using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using PinyinGloss.CandidateReader;
using ListBox = System.Windows.Controls.ListBox;
using Orientation = System.Windows.Controls.Orientation;

namespace PinyinGloss.Windows.Tests;

[Trait("Category", "InteractiveDesktop")]
public class CandidatePageTests
{
    [Fact]
    public async Task Separate_candidate_numbers_are_used_instead_of_guessing_from_position()
    {
        using var fixture = await PageWindow.Create(("2", "保险"), ("3", "保鲜"), ("1", "包线"));
        var page = UiaCandidateScanner.ReadCandidates(fixture.Container, 5);
        Assert.Equal(new[] { 2, 3, 1 }, page.Candidates.Select(c => c.VisibleIndex));
        Assert.Equal(new[] { "保险", "保鲜", "包线" }, page.Candidates.Select(c => c.Text));
    }

    [Fact]
    public async Task Literal_pinyin_candidate_does_not_invalidate_the_chinese_candidates()
    {
        using var fixture = await PageWindow.Create(("1", "平"), ("2", "ping"), ("3", "苹果"));
        var page = UiaCandidateScanner.ReadCandidates(fixture.Container, 5);
        Assert.Equal(new[] { "平", "ping", "苹果" }, page.Candidates.Select(c => c.Text));
        Assert.Equal(new[] { 1, 2, 3 }, page.Candidates.Select(c => c.VisibleIndex));
    }

    [Fact]
    public async Task Candidate_without_a_visible_number_is_rejected()
    {
        using var fixture = await PageWindow.Create(("", "保险"));
        Assert.Empty(UiaCandidateScanner.ReadCandidates(fixture.Container, 5).Candidates);
    }

    [Fact]
    public async Task Duplicate_number_beyond_display_limit_rejects_the_page()
    {
        using var fixture = await PageWindow.Create(("1", "保险"), ("2", "保鲜"), ("2", "包线"));
        Assert.Empty(UiaCandidateScanner.ReadCandidates(fixture.Container, 2).Candidates);
    }

    private sealed class PageWindow(Window window, AutomationElement container) : IDisposable
    {
        internal AutomationElement Container { get; } = container;
        public void Dispose() => window.Dispatcher.Invoke(() => { window.Close(); Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); });

        internal static async Task<PageWindow> Create(params (string Index, string Text)[] items)
        {
            var ready = new TaskCompletionSource<(Window Window, nint Handle)>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    var list = new ListBox();
                    AutomationProperties.SetAutomationId(list, "IME_Candidate_Window");
                    foreach (var (index, text) in items)
                    {
                        var panel = new StackPanel { Orientation = Orientation.Horizontal };
                        var number = new CandidatePart { Text = index, Width = 30 };
                        AutomationProperties.SetAutomationId(number, "TEMPLATE_PART_CandidateItemIndex");
                        var label = new CandidatePart { Text = text };
                        AutomationProperties.SetAutomationId(label, "TEMPLATE_PART_CandidateItemText");
                        panel.Children.Add(number); panel.Children.Add(label);
                        var item = new ListBoxItem { Content = panel };
                        AutomationProperties.SetName(item, text);
                        list.Items.Add(item);
                    }
                    var window = new Window { Content = list, Width = 400, Height = 250, ShowActivated = false, ShowInTaskbar = false };
                    window.Show(); window.UpdateLayout();
                    ready.SetResult((window, new WindowInteropHelper(window).Handle));
                    Dispatcher.Run();
                }
                catch (Exception error) { ready.TrySetException(error); }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            var created = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                var root = AutomationElement.FromHandle(created.Handle);
                var container = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "IME_Candidate_Window"));
                Assert.NotNull(container);
                return new(created.Window, container);
            }
            catch { created.Window.Dispatcher.Invoke(() => { created.Window.Close(); Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }); throw; }
        }
    }
    private sealed class CandidatePart : TextBlock
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new CandidatePartPeer(this);
    }
    private sealed class CandidatePartPeer(CandidatePart owner) : TextBlockAutomationPeer(owner)
    {
        protected override bool IsControlElementCore() => false;
        protected override bool IsContentElementCore() => false;
    }
}
