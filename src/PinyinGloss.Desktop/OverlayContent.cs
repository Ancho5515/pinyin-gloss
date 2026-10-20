using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PinyinGloss.Core;

namespace PinyinGloss.Desktop;

// 不创建 HWND 的内容层，供释义窗使用并可离屏回归。
internal sealed class OverlayContent
{
    private Page? _displayed, _pending;
    private bool _acceptResults;
    private OverlayTheme? _theme;
    internal Grid? Displayed => _displayed?.Grid;
    internal bool SetSnapshot(CandidateSnapshot snapshot, AppSettings settings)
    {
        var page = new Page(snapshot, settings);
        if (_theme != null) page.ApplyTheme(_theme);
        _acceptResults = true;
        if (_displayed == null || page.Complete) { _displayed = page; _pending = null; return true; }
        _pending = page;
        return false;
    }
    internal void BeginUpdate() { _pending = null; _acceptResults = false; }
    internal void Invalidate() { _displayed = _pending = null; _acceptResults = false; }
    internal bool UpdateCell(TranslationCell cell)
    {
        if (!_acceptResults || (_pending ?? _displayed)?.UpdateCell(cell) != true) return false;
        if (_pending == null) return true;
        if (!_pending.Complete) return false;
        _displayed = _pending; _pending = null;
        return true;
    }
    internal void ApplyTheme(OverlayTheme theme)
    {
        _theme = theme; _displayed?.ApplyTheme(theme); _pending?.ApplyTheme(theme);
    }

    private sealed class Page
    {
        private readonly CandidateSnapshot _snapshot;
        private readonly AppSettings _settings;
        private readonly Dictionary<(string, string), TextBlock> _cells = [];
        private readonly HashSet<(string, string)> _received = [];
        internal Grid Grid { get; } = new();
        internal bool Complete => _cells.Count == _received.Count;
        internal Page(CandidateSnapshot snapshot, AppSettings settings)
        {
            _snapshot = snapshot; _settings = settings;
            var columns = settings.Columns.Where(c => c.Enabled).ToArray();
            Grid.ColumnDefinitions.Add(new() { Width = new GridLength(125) });
            foreach (var column in columns) Grid.ColumnDefinitions.Add(new() { Width = new GridLength(column.Width, GridUnitType.Star) });
            TextBlock Add(string text, int row, int col, ColumnSettings? column = null)
            {
                var block = new TextBlock { Text = text, Tag = column, Margin = new(6, 5, 6, 5), TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 70, TextTrimming = TextTrimming.CharacterEllipsis };
                System.Windows.Controls.Grid.SetRow(block, row); System.Windows.Controls.Grid.SetColumn(block, col);
                Grid.Children.Add(block); return block;
            }
            Grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Add(snapshot.CaptureMethod == "Demo" ? "模拟候选" : "微软候选", 0, 0);
            for (var col = 0; col < columns.Length; col++)
                Add($"{settings.Providers.First(p => p.Id == columns[col].SourceId).Name}\n{LanguageCatalog.Name(columns[col].TargetLanguage)}", 0, col + 1, columns[col]);
            var words = snapshot.Candidates.Take(settings.Capture.MaxCandidates).ToArray();
            for (var row = 0; row < words.Length; row++)
            {
                Grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                Add($"{words[row].VisibleIndex}  {words[row].Text}", row + 1, 0);
                for (var col = 0; col < columns.Length; col++)
                    _cells[(words[row].Id, columns[col].Id)] = Add("等待…", row + 1, col + 1, columns[col]);
            }
        }
        internal bool UpdateCell(TranslationCell cell)
        {
            if (_snapshot.SnapshotId != cell.SnapshotId || _snapshot.Generation != cell.Generation || _settings.SettingsRevision != cell.SettingsRevision) return false;
            if (!_cells.TryGetValue((cell.Result.CandidateId, cell.ColumnId), out var block)) return false;
            var column = _settings.Columns.First(c => c.Id == cell.ColumnId);
            if (column.TargetLanguage != cell.TargetLanguage || cell.Result.TargetLanguage != column.TargetLanguage ||
                !_snapshot.Candidates.Any(c => c.Id == cell.Result.CandidateId && c.Text == cell.Result.OriginalText)) return false;
            var source = _settings.Providers.FirstOrDefault(p => p.Id == cell.ActualSourceId);
            block.Text = cell.Result.Status == ResultStatus.Ready ? cell.Result.Gloss + (cell.ActualSourceId != column.SourceId ? $"\n[{source?.Name ?? cell.ActualSourceId}]" : "") : cell.Result.Status switch
            { ResultStatus.NotFound => "无释义", ResultStatus.Timeout => "超时", ResultStatus.InvalidOutput => "输出无效", ResultStatus.Unavailable => "来源未就绪", ResultStatus.Skipped => "已跳过", ResultStatus.Cancelled => "已取消", _ => "等待…" };
            _received.Add((cell.Result.CandidateId, cell.ColumnId)); return true;
        }
        internal void ApplyTheme(OverlayTheme theme)
        {
            foreach (var block in Grid.Children.OfType<TextBlock>())
                block.Foreground = new SolidColorBrush(block.Tag is ColumnSettings column ? theme.ColumnColor(column) : theme.Foreground);
        }
    }
}
