using System.Runtime.InteropServices;
using System.Threading.Channels;
using UIA = Interop.UIAutomationClient;

namespace PinyinGloss.CandidateReader;

/// <summary>Finds input-host HWNDs omitted by desktop window enumeration. No text is read in callbacks.</summary>
internal sealed class UiaEventWindowDiscovery : IDisposable
{
    private readonly UIA.IUIAutomation _automation = new UIA.CUIAutomation8();
    private readonly Channel<ObservedEvent> _pending = Channel.CreateBounded<ObservedEvent>(new BoundedChannelOptions(32)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly EventHandler _handler;
    private int _processed, _trusted, _resolved;
    private string? _lastError;
    internal string Diagnostics => $"UIA 事件：收到={Volatile.Read(ref _handler.Count)}，处理={_processed}，宿主={_trusted}，窗口={_resolved}" + (_lastError == null ? "" : $"，错误={_lastError}");
    private sealed record ObservedEvent(UIA.IUIAutomationElement Element, nint Foreground, int EventId);

    internal UiaEventWindowDiscovery()
    {
        _handler = new(_pending.Writer);
        try
        {
            var desktop = _automation.GetRootElement();
            foreach (var id in new[] { 20003, 20016, 20012 }) // MenuOpened, WindowOpened, SelectionItem selected.
                _automation.AddAutomationEventHandler(id, desktop, UIA.TreeScope.TreeScope_Subtree, null, _handler);
        }
        catch { _automation.RemoveAllEventHandlers(); throw; }
    }

    internal IReadOnlyList<nint> TakeWindows(nint foreground)
    {
        var windows = new List<nint>();
        while (_pending.Reader.TryRead(out var notification))
        {
            _processed++;
            if (notification.Foreground != foreground) continue;
            try
            {
                var element = notification.Element; var pid = element.CurrentProcessId;
                if (!UiaCandidateScanner.IsMicrosoftInputHostProcess(pid)) continue;
                _trusted++;
                var candidateSeen = false;
                for (var depth = 0; element != null && depth < 8; depth++)
                {
                    if (element.CurrentProcessId != pid) break;
                    candidateSeen |= element.CurrentAutomationId is "IME_Candidate_Window" or "IME_Prediction_Window";
                    var hwnd = element.CurrentNativeWindowHandle;
                    if (hwnd != 0 && (candidateSeen || notification.EventId == 20016))
                    {
                        Native.GetWindowThreadProcessId(hwnd, out var owner);
                        if (owner == pid && Native.IsWindowVisible(hwnd) && UiaCandidateScanner.IsMicrosoftInputHost(hwnd)) { windows.Add(hwnd); _resolved++; }
                        break;
                    }
                    element = _automation.RawViewWalker.GetParentElement(element);
                }
            }
            catch (COMException error) { _lastError = error.HResult.ToString("x8"); } // Removed providers are expected during closing and focus changes.
        }
        return windows;
    }

    public void Dispose()
    {
        _pending.Writer.TryComplete();
        try { _automation.RemoveAllEventHandlers(); }
        catch (COMException) { }
        GC.KeepAlive(_handler);
    }

    [ComVisible(true)]
    private sealed class EventHandler(ChannelWriter<ObservedEvent> writer) : UIA.IUIAutomationEventHandler
    {
        internal int Count;
        public void HandleAutomationEvent(UIA.IUIAutomationElement sender, int eventId)
        {
            Interlocked.Increment(ref Count);
            writer.TryWrite(new(sender, Native.GetForegroundWindow(), eventId));
        }
    }
}
