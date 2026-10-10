using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using PinyinGloss.Core;

namespace PinyinGloss.CandidateReader;
/// <summary>Experimental, conservative reader: Microsoft input hosts and explicitly identified candidate containers only.</summary>
internal sealed partial class UiaCandidateScanner
{
    private nint _knownWindow; private nint _lastForeground; private string? _fingerprint;
    private long _generation; private readonly string _session = Guid.NewGuid().ToString("N");
    internal string Diagnostics { get; private set; } = "尚未发现可识别的候选容器";
    [GeneratedRegex(@"^\s*(?:(?<index>\d{1,2})[.、\s]+)?(?<text>[^\r\n]+?)\s*$")]
    private static partial Regex CandidateRegex();
    internal CandidateSnapshot? Scan(int maxCandidates)
    {
        var foreground = Native.GetForegroundWindow();
        if (foreground != _lastForeground) { _knownWindow = 0; _fingerprint = null; _lastForeground = foreground; _generation++; }
        var windows = new List<nint>();
        if (_knownWindow != 0 && Native.IsWindowVisible(_knownWindow)) windows.Add(_knownWindow);
        else Native.EnumWindows((hwnd, _) => { if (Native.IsWindowVisible(hwnd) && IsMicrosoftInputHost(hwnd)) windows.Add(hwnd); return true; }, 0);
        foreach (var hwnd in windows)
        {
            try
            {
                if (!IsMicrosoftInputHost(hwnd)) continue;
                var root = AutomationElement.FromHandle(hwnd); if (root == null) continue;
                // Traversal is bounded and happens exclusively inside an approved input host, never inside the input application.
                var container = Walk(root, 4, 150).FirstOrDefault(IsCandidateContainer);
                if (container == null) { Diagnostics = $"宿主 {Native.WindowClass(hwnd)} 可访问，但未识别 candidate 容器"; continue; }
                var items = Walk(container, 3, 80).Where(e => e.Current.ControlType == ControlType.ListItem && !e.Current.IsOffscreen).ToArray();
                if (items.Length == 0) items = Walk(container, 2, 50).Where(e => e.Current.ControlType == ControlType.Text && !e.Current.IsOffscreen).ToArray();
                var candidates = new List<Candidate>(); string? selected = null;
                foreach (var element in items.OrderBy(e => e.Current.BoundingRectangle.Top).ThenBy(e => e.Current.BoundingRectangle.Left))
                {
                    var name = element.Current.Name;
                    if (string.IsNullOrWhiteSpace(name)) name = string.Join("", Walk(element, 1, 8).Where(e => e.Current.ControlType == ControlType.Text).Select(e => e.Current.Name));
                    var match = CandidateRegex().Match(name); if (!match.Success) continue;
                    var text = match.Groups["text"].Value;
                    if (text.Length > 64 || !text.Any(c => c is >= '\u3400' and <= '\u9fff') || text.Contains("  ") || text.Contains('\t')) continue;
                    var index = match.Groups["index"].Success ? int.Parse(match.Groups["index"].Value) : candidates.Count + 1;
                    if (index is < 1 or > 10) continue;
                    var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{index}:{text}")))[..16];
                    candidates.Add(new(id, text, index));
                    if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern) && ((SelectionItemPattern)pattern).Current.IsSelected) selected = id;
                    if (candidates.Count >= maxCandidates) break;
                }
                if (candidates.Count == 0 || candidates.Select(c => c.VisibleIndex).Distinct().Count() != candidates.Count) continue;
                // Recheck after potentially slow provider calls: no stale foreground association.
                if (Native.GetForegroundWindow() != foreground || !Native.IsWindowVisible(hwnd)) return null;
                var rect = container.Current.BoundingRectangle;
                if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) continue;
                Native.GetWindowThreadProcessId(hwnd, out var pid);
                using var process = Process.GetProcessById((int)pid);
                var identity = $"{foreground:x}:{hwnd:x}:{pid}:{process.StartTime.ToUniversalTime().Ticks}";
                var fingerprint = identity + string.Join('|', candidates.Select(c => c.Id)) + selected;
                if (_fingerprint != fingerprint) { _generation++; _fingerprint = fingerprint; }
                _knownWindow = hwnd; Diagnostics = $"UIA 识别到 {candidates.Count} 个可见候选";
                return new(_session, $"{_session}-{_generation}", _generation, identity, new(rect.X, rect.Y, rect.Width, rect.Height), candidates, selected);
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            { Diagnostics = "UIA: " + ex.GetType().Name; }
        }
        _knownWindow = 0; _fingerprint = null; return null;
    }
    private static bool IsCandidateContainer(AutomationElement e)
    {
        var info = e.Current;
        if (info.IsOffscreen) return false;
        return info.AutomationId.Contains("candidate", StringComparison.OrdinalIgnoreCase) ||
            info.ControlType == ControlType.List && (info.Name.Contains("候选", StringComparison.Ordinal) || info.Name.Contains("candidate", StringComparison.OrdinalIgnoreCase));
    }
    private static IEnumerable<AutomationElement> Walk(AutomationElement root, int depth, int budget)
    {
        var queue = new Queue<(AutomationElement Element, int Depth)>(); queue.Enqueue((root, 0)); var count = 0;
        var walker = TreeWalker.ControlViewWalker;
        while (queue.Count > 0 && count++ < budget)
        {
            var (element, level) = queue.Dequeue(); yield return element;
            if (level >= depth) continue;
            var child = walker.GetFirstChild(element); var siblings = 0;
            while (child != null && siblings++ < budget && queue.Count < budget) { queue.Enqueue((child, level + 1)); child = walker.GetNextSibling(child); }
        }
    }
    private static bool IsMicrosoftInputHost(nint hwnd)
    {
        try
        {
            Native.GetWindowThreadProcessId(hwnd, out var pid); using var process = Process.GetProcessById((int)pid);
            if (process.ProcessName is not ("TextInputHost" or "InputApp" or "MicrosoftIme" or "ChsIME")) return false;
            var file = process.MainModule?.FileName; if (file == null) return false;
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return file.StartsWith(Path.Combine(windows, "SystemApps") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                file.StartsWith(Path.Combine(windows, "System32", "InputMethod") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
