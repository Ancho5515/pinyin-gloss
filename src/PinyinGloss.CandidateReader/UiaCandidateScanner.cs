using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using System.Globalization;
using System.Runtime.InteropServices;
using PinyinGloss.Core;

namespace PinyinGloss.CandidateReader;
/// <summary>Experimental, conservative reader: Microsoft input hosts and explicitly identified candidate containers only.</summary>
internal sealed partial class UiaCandidateScanner : IDisposable
{
    private readonly UiaEventWindowDiscovery? _events;
    private readonly CandidatePageStability _page = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    internal bool IsConfirmingPage => _page.IsConfirming;
    private nint _knownWindow; private nint _lastForeground; private string? _fingerprint;
    private long _generation; private readonly string _session = Guid.NewGuid().ToString("N");
    internal string Diagnostics { get; private set; } = "尚未发现可识别的候选容器";
    [GeneratedRegex(@"^\s*(?:(?<index>\d{1,2})[.、\s]+)?(?<text>[^\r\n]+?)\s*$")]
    private static partial Regex CandidateRegex();
    internal UiaCandidateScanner()
    {
        try { _events = new(); Diagnostics = "UIA 原生事件监听已启动，等待候选事件"; }
        catch (COMException) { Diagnostics = "UIA 原生事件订阅不可用，继续有限窗口扫描"; }
    }
    public void Dispose() => _events?.Dispose();
    internal static (IReadOnlyList<Candidate> Candidates, string? SelectedId) ReadCandidates(AutomationElement container, int maxCandidates)
    {
        var items = Walk(container, 3, 80).Where(e => e.Current.ControlType == ControlType.ListItem && !e.Current.IsOffscreen).ToArray();
        if (items.Length == 0) items = Walk(container, 2, 50).Where(e => e.Current.ControlType == ControlType.Text && !e.Current.IsOffscreen).ToArray();
        var candidates = new List<Candidate>(); string? selected = null;
        foreach (var element in items.OrderBy(e => e.Current.BoundingRectangle.Top).ThenBy(e => e.Current.BoundingRectangle.Left))
        {
            var parts = Walk(element, 1, 8, TreeWalker.RawViewWalker).ToArray();
            var indexText = parts.FirstOrDefault(e => e.Current.AutomationId == "TEMPLATE_PART_CandidateItemIndex")?.Current.Name;
            var text = parts.FirstOrDefault(e => e.Current.AutomationId == "TEMPLATE_PART_CandidateItemText")?.Current.Name;
            var match = CandidateRegex().Match(element.Current.Name);
            indexText ??= match.Success && match.Groups["index"].Success ? match.Groups["index"].Value : null;
            text ??= match.Success ? match.Groups["text"].Value : null;
            // Explicit visible numbers are required; position alone must never invent a correspondence.
            if (!int.TryParse(indexText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index is < 1 or > 10 ||
                string.IsNullOrWhiteSpace(text) || text.Length > 64 ||
                text.Any(char.IsControl) || text.Contains("  ")) return ([], null);
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{index}:{text}")))[..16];
            candidates.Add(new(id, text, index));
            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern) && ((SelectionItemPattern)pattern).Current.IsSelected) selected = id;
        }
        // Validate the entire visible page before applying the configured display limit.
        if (candidates.Count is 0 or > 10 || candidates.Select(c => c.VisibleIndex).Distinct().Count() != candidates.Count) return ([], null);
        var visible = candidates.Take(maxCandidates).ToArray();
        return (visible, visible.Any(c => c.Id == selected) ? selected : null);
    }
    internal CandidateSnapshot? Scan(int maxCandidates)
    {
        var foreground = Native.GetForegroundWindow();
        if (foreground != _lastForeground) { _knownWindow = 0; _fingerprint = null; _page.Reset(); _lastForeground = foreground; _generation++; }
        var windows = _events?.TakeWindows(foreground).Distinct().ToList() ?? [];
        if (_knownWindow != 0 && Native.IsWindowVisible(_knownWindow) && !windows.Contains(_knownWindow)) windows.Add(_knownWindow);
        if (windows.Count == 0) Native.EnumWindows((hwnd, _) => { if (Native.IsWindowVisible(hwnd) && IsMicrosoftInputHost(hwnd)) windows.Add(hwnd); return true; }, 0);
        foreach (var hwnd in windows)
        {
            try
            {
                if (!IsMicrosoftInputHost(hwnd)) continue;
                var root = AutomationElement.FromHandle(hwnd); if (root == null) continue;
                // Traversal is bounded and happens exclusively inside an approved input host, never inside the input application.
                var container = Walk(root, 4, 150).FirstOrDefault(IsCandidateContainer);
                if (container == null) { Diagnostics = $"宿主 {Native.WindowClass(hwnd)} 可访问，但未识别 candidate 容器"; continue; }
                var (candidates, selected) = ReadCandidates(container, maxCandidates);
                if (candidates.Count == 0) continue;
                // Recheck after potentially slow provider calls: no stale foreground association.
                if (Native.GetForegroundWindow() != foreground || !Native.IsWindowVisible(hwnd)) { _page.Reset(); return null; }
                var rect = container.Current.BoundingRectangle;
                if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) continue;
                Native.GetWindowThreadProcessId(hwnd, out var pid);
                using var process = Process.GetProcessById((int)pid);
                var identity = $"{foreground:x}:{hwnd:x}:{pid}:{process.StartTime.ToUniversalTime().Ticks}";
                var fingerprint = identity + string.Join('|', candidates.Select(c => c.Id)) + selected;
                _knownWindow = hwnd;
                // UIA properties are not an atomic page read. Reject transient mixtures during IME updates.
                if (!_page.Observe(fingerprint, _clock.Elapsed)) { _fingerprint = null; Diagnostics = "UIA 候选页变化中，等待一致读取"; return null; }
                if (_fingerprint != fingerprint) { _generation++; _fingerprint = fingerprint; }
                _knownWindow = hwnd; Diagnostics = $"UIA 识别到 {candidates.Count} 个可见候选";
                return new(_session, $"{_session}-{_generation}", _generation, identity, new(rect.X, rect.Y, rect.Width, rect.Height), candidates, selected);
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            { Diagnostics = "UIA: " + ex.GetType().Name; }
        }
        _knownWindow = 0; _fingerprint = null; _page.Reset();
        if (_events != null) Diagnostics = "当前未取得可信候选快照；" + _events.Diagnostics;
        return null;
    }
    private static bool IsCandidateContainer(AutomationElement e)
    {
        var info = e.Current;
        if (info.IsOffscreen) return false;
        return info.AutomationId is "IME_Candidate_Window" or "IME_Prediction_Window" ||
            info.ControlType == ControlType.List && (info.Name.Contains("候选", StringComparison.Ordinal) || info.Name.Contains("candidate", StringComparison.OrdinalIgnoreCase));
    }
    private static IEnumerable<AutomationElement> Walk(AutomationElement root, int depth, int budget, TreeWalker? treeWalker = null)
    {
        var queue = new Queue<(AutomationElement Element, int Depth)>(); queue.Enqueue((root, 0)); var count = 0;
        var walker = treeWalker ?? TreeWalker.ControlViewWalker; var pid = root.Current.ProcessId;
        while (queue.Count > 0 && count++ < budget)
        {
            var (element, level) = queue.Dequeue(); if (element.Current.ProcessId != pid) continue; yield return element;
            if (level >= depth) continue;
            var child = walker.GetFirstChild(element); var siblings = 0;
            while (child != null && siblings++ < budget && queue.Count < budget) { queue.Enqueue((child, level + 1)); child = walker.GetNextSibling(child); }
        }
    }
    internal static bool IsMicrosoftInputHost(nint hwnd)
    {
        try
        {
            Native.GetWindowThreadProcessId(hwnd, out var pid); return IsMicrosoftInputHostProcess((int)pid);
        }
        catch { return false; }
    }
    internal static bool IsMicrosoftInputHostProcess(int pid)
    {
        try
        {
            using var process = Native.OpenProcess(0x1000, false, (uint)pid); if (process.IsInvalid) return false;
            var path = new StringBuilder(32768); uint size = (uint)path.Capacity;
            if (!Native.QueryFullProcessImageName(process, 0, path, ref size)) return false;
            var file = path.ToString();
            if (Path.GetFileNameWithoutExtension(file) is not ("TextInputHost" or "InputApp" or "MicrosoftIme" or "ChsIME")) return false;
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return file.StartsWith(Path.Combine(windows, "SystemApps") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                file.StartsWith(Path.Combine(windows, "System32", "InputMethod") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
