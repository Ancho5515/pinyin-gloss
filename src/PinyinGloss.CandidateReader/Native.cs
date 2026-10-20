using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PinyinGloss.CandidateReader;
internal static class Native
{
    internal delegate bool EnumWindowCallback(nint hwnd, nint lParam);
    internal delegate void WinEventCallback(nint hook, uint evt, nint hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowCallback callback, nint param);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)] internal static extern nint SendMessageTimeout(nint hwnd, uint message, nuint wparam, nint lparam, uint flags, uint timeout, out nuint result);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref uint size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint hwnd, StringBuilder name, int count);
    [DllImport("user32.dll")] internal static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventCallback callback, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] internal static extern bool PeekMessage(out Message message, nint hwnd, uint min, uint max, uint flags);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] internal static extern nint DispatchMessage(ref Message message);
    [StructLayout(LayoutKind.Sequential)] internal struct Message { public nint Hwnd; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [StructLayout(LayoutKind.Sequential)] internal struct GuiThreadInfo
    { public int Size, Flags; public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret; public int Left, Top, Right, Bottom; }
    internal static string WindowClass(nint hwnd) { var text = new StringBuilder(256); GetClassName(hwnd, text, text.Capacity); return text.ToString(); }
}
