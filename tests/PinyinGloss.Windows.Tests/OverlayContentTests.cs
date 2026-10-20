using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PinyinGloss.Core;
using PinyinGloss.Desktop;

namespace PinyinGloss.Windows.Tests;

public class OverlayContentTests
{
    private static AppSettings Settings() => new()
    {
        Providers = [new() { Id = "dict", Name = "词典", Kind = ProviderKind.LocalDictionary }],
        Columns = [new() { Id = "en", SourceId = "dict", TargetLanguage = "en" }]
    };
    private static CandidateSnapshot Page(long generation) => new("session", $"page-{generation}", generation,
        "identity", new(10, 10, 100, 30), [new("apple", "苹果", 1), new("insurance", "保险", 2)]);
    private static TranslationCell Cell(long generation, string id, ResultStatus status = ResultStatus.Ready) =>
        new($"page-{generation}", generation, 1, "en", "en", "dict", "v1", new(id, id == "apple" ? "苹果" : "保险", "en", status, $"gloss-{generation}-{id}"));
    private static string[] Texts(Grid? grid) => grid!.Children.OfType<TextBlock>().Select(b => b.Text).ToArray();

    [Fact]
    public Task New_page_keeps_previous_grid_until_every_cell_has_a_result() => OnSta(() =>
    {
        var view = new OverlayContent(); view.SetSnapshot(Page(1), Settings());
        view.UpdateCell(Cell(1, "apple")); view.UpdateCell(Cell(1, "insurance"));
        var oldGrid = view.Displayed; var oldTexts = Texts(oldGrid);
        Assert.False(view.SetSnapshot(Page(2), Settings()));
        Assert.False(view.UpdateCell(Cell(2, "apple")));
        Assert.Same(oldGrid, view.Displayed); Assert.Equal(oldTexts, Texts(view.Displayed));
        Assert.True(view.UpdateCell(Cell(2, "insurance", ResultStatus.NotFound)));
        Assert.NotSame(oldGrid, view.Displayed); Assert.Contains("gloss-2-apple", Texts(view.Displayed));
        Assert.Contains("无释义", Texts(view.Displayed)); Assert.DoesNotContain("等待…", Texts(view.Displayed));
    });

    [Fact]
    public Task Updating_blocks_old_results_and_replaces_superseded_pending_page() => OnSta(() =>
    {
        var view = new OverlayContent(); view.SetSnapshot(Page(1), Settings()); view.UpdateCell(Cell(1, "apple"));
        var old = Texts(view.Displayed); view.BeginUpdate();
        Assert.False(view.UpdateCell(Cell(1, "insurance"))); Assert.Equal(old, Texts(view.Displayed));
        view.SetSnapshot(Page(2), Settings()); view.UpdateCell(Cell(2, "apple")); view.BeginUpdate();
        view.SetSnapshot(Page(3), Settings());
        Assert.False(view.UpdateCell(Cell(2, "insurance")));
        Assert.False(view.UpdateCell(Cell(3, "apple")));
        Assert.True(view.UpdateCell(Cell(3, "insurance", ResultStatus.Timeout)));
        Assert.Contains("gloss-3-apple", Texts(view.Displayed)); Assert.Contains("超时", Texts(view.Displayed));
    });

    [Fact]
    public Task Hidden_discards_all_pages_and_late_results_cannot_restore_them() => OnSta(() =>
    {
        var view = new OverlayContent(); view.SetSnapshot(Page(1), Settings()); view.SetSnapshot(Page(2), Settings());
        view.Invalidate(); Assert.Null(view.Displayed); Assert.False(view.UpdateCell(Cell(2, "apple")));
        Assert.True(view.SetSnapshot(Page(3), Settings())); Assert.Contains("等待…", Texts(view.Displayed));
    });

    [Fact]
    public Task Wrong_word_language_generation_and_revision_cannot_complete_pending_page() => OnSta(() =>
    {
        var view = new OverlayContent(); view.SetSnapshot(Page(1), Settings()); var old = view.Displayed;
        view.SetSnapshot(Page(2), Settings()); var cell = Cell(2, "apple");
        Assert.False(view.UpdateCell(cell with { Generation = 1 }));
        Assert.False(view.UpdateCell(cell with { SettingsRevision = 2 }));
        Assert.False(view.UpdateCell(cell with { Result = cell.Result with { OriginalText = "保鲜" } }));
        Assert.False(view.UpdateCell(cell with { Result = cell.Result with { TargetLanguage = "ja" } }));
        Assert.Same(old, view.Displayed);
    });

    [Fact]
    public Task Theme_recolors_displayed_and_pending_pages_without_replacing_texts() => OnSta(() =>
    {
        var settings = Settings(); var view = new OverlayContent(); view.SetSnapshot(Page(1), settings);
        view.UpdateCell(Cell(1, "apple")); var old = view.Displayed; var texts = Texts(old);
        view.SetSnapshot(Page(2), settings);
        var dark = OverlayTheme.Create(settings.Display, true, false); view.ApplyTheme(dark);
        Assert.Same(old, view.Displayed); Assert.Equal(texts, Texts(old));
        Assert.All(old!.Children.OfType<TextBlock>().Where(b => b.Tag == null), b => Assert.Equal(dark.Foreground, ((SolidColorBrush)b.Foreground).Color));
        view.UpdateCell(Cell(2, "apple")); view.UpdateCell(Cell(2, "insurance"));
        Assert.All(view.Displayed!.Children.OfType<TextBlock>().Where(b => b.Tag == null), b => Assert.Equal(dark.Foreground, ((SolidColorBrush)b.Foreground).Color));
    });

    [Fact]
    public Task Model_waiting_commits_new_page_then_gloss_updates_same_grid() => OnSta(() =>
    {
        var view = new OverlayContent(); view.SetSnapshot(Page(1), Settings());
        view.UpdateCell(Cell(1, "apple")); view.UpdateCell(Cell(1, "insurance"));
        var old = view.Displayed; view.SetSnapshot(Page(2), Settings());
        Assert.False(view.UpdateCell(Cell(2, "apple", ResultStatus.Pending)));
        Assert.True(view.UpdateCell(Cell(2, "insurance", ResultStatus.Pending)));
        Assert.NotSame(old, view.Displayed); var current = view.Displayed;
        Assert.Contains("等待…", Texts(current));
        Assert.True(view.UpdateCell(Cell(2, "apple")));
        Assert.Same(current, view.Displayed); Assert.Contains("gloss-2-apple", Texts(current));
        Assert.False(view.UpdateCell(Cell(1, "insurance")));
    });

    private static Task OnSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); completion.SetResult(); } catch (Exception ex) { completion.SetException(ex); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
