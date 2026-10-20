using System.Diagnostics;
using System.Windows.Automation;
using PinyinGloss.Core;
using UIA = Interop.UIAutomationClient;

namespace PinyinGloss.CandidateReader;

internal sealed record ResolvedTextTarget(UIA.IUIAutomationElement? FocusedElement, long ForegroundWindow, nint NativeFocusWindow,
    int ProcessId, string ProcessName, string ClassName, string FrameworkId, int ControlType, string Identity, bool Password);
internal sealed record TextTargetResolution(ResolvedTextTarget? Target, TextReadStatus Status, long ForegroundWindow);

internal sealed class TextTargetResolver
{
    internal UIA.IUIAutomation Automation { get; } = new UIA.CUIAutomation8();
    internal static bool CanReadTarget(bool password, bool foreground, bool hasText) => !password && foreground && hasText;
    internal static bool CanReadTextFocus(int type, bool ownTextPattern, bool selection = false) =>
        type == ControlType.Edit.Id || type == ControlType.Text.Id || type == ControlType.Document.Id ||
        selection && type == ControlType.Group.Id ||
        ownTextPattern && (type == ControlType.Custom.Id || type == ControlType.Pane.Id);
    internal static TextReadStatus UnreadableFocusStatus(int type, long elementWindow, long nativeFocus, long caretWindow, long foreground) =>
        type == ControlType.Window.Id && elementWindow != 0 && elementWindow == foreground &&
        elementWindow == nativeFocus && elementWindow == caretWindow ? TextReadStatus.UnsupportedControl : TextReadStatus.NoFocusedTarget;
    internal TextTargetResolution Resolve(TextReadRequest request)
    {
        var foreground = Native.GetForegroundWindow();
        if (foreground == 0) return new(null, TextReadStatus.NoFocusedTarget, 0);
        var focused = Automation.GetFocusedElement();
        if (focused == null) return new(null, TextReadStatus.NoFocusedTarget, foreground.ToInt64());
        if (focused.CurrentIsPassword != 0) return new(null, TextReadStatus.PasswordTarget, foreground.ToInt64());
        var className = focused.CurrentClassName;
        var legacy = UiaTextProvider.Pattern<UIA.IUIAutomationLegacyIAccessiblePattern>(focused, 10018);
        if (legacy != null && (legacy.CurrentState & 0x20000000) != 0) return new(null, TextReadStatus.PasswordTarget, foreground.ToInt64());
        var legacyText = legacy is { CurrentChildId: 0, CurrentRole: 42 or 15 } && (legacy.CurrentState & 4) != 0;
        var gui = new Native.GuiThreadInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<Native.GuiThreadInfo>() };
        var hasGui = Native.GetGUIThreadInfo(Native.GetWindowThreadProcessId(foreground, out _), ref gui);
        if (!CanReadTextFocus(focused.CurrentControlType, UiaTextProvider.Pattern<UIA.IUIAutomationTextPattern>(focused, 10014) != null, request.Selection) && className != "Scintilla" && !legacyText)
            return new(null, hasGui ? UnreadableFocusStatus(focused.CurrentControlType, focused.CurrentNativeWindowHandle.ToInt64(),
                gui.Focus.ToInt64(), gui.Caret.ToInt64(), foreground.ToInt64()) : TextReadStatus.NoFocusedTarget, foreground.ToInt64());
        if (!InForeground(focused, foreground)) return new(null, TextReadStatus.TargetChanged, foreground.ToInt64());
        var native = (nint)focused.CurrentNativeWindowHandle;
        if (hasGui && gui.Focus != 0)
            native = gui.Focus;
        using var process = Process.GetProcessById(focused.CurrentProcessId);
        var identity = $"{foreground:X}/{string.Join('.', focused.GetRuntimeId())}";
        return new(new(focused, foreground.ToInt64(), native, focused.CurrentProcessId, process.ProcessName, className,
            focused.CurrentFrameworkId, focused.CurrentControlType, identity, false), TextReadStatus.Success, foreground.ToInt64());
    }
    internal bool IsCurrent(ResolvedTextTarget target)
    {
        if (Native.GetForegroundWindow().ToInt64() != target.ForegroundWindow) return false;
        var focused = Automation.GetFocusedElement();
        return focused != null && focused.CurrentIsPassword == 0 &&
            $"{target.ForegroundWindow:X}/{string.Join('.', focused.GetRuntimeId())}" == target.Identity;
    }
    internal bool InForeground(UIA.IUIAutomationElement element, nint foreground)
    {
        // 深层网页编辑区可隔着多层布局节点；这里只核对窗口身份，不借用父级正文。
        for (var depth = 0; element != null && depth < 64; depth++)
        {
            var hwnd = (nint)element.CurrentNativeWindowHandle;
            if (hwnd != 0 && Native.GetAncestor(hwnd, 2) == foreground) return true;
            element = Automation.RawViewWalker.GetParentElement(element);
        }
        return false;
    }
}
