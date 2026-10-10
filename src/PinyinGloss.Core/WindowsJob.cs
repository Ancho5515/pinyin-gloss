using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PinyinGloss.Core;
/// <summary>Closing this handle, including on a host crash, terminates assigned child processes.</summary>
public sealed class WindowsJob : IDisposable
{
    private nint _handle;
    public WindowsJob()
    {
        if (!OperatingSystem.IsWindows()) return;
        _handle = CreateJobObject(0, null);
        if (_handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var info = new ExtendedLimits { BasicLimitInformation = new() { LimitFlags = 0x2000 } };
        if (!SetInformationJobObject(_handle, 9, ref info, (uint)Marshal.SizeOf<ExtendedLimits>()))
        { var error = Marshal.GetLastWin32Error(); Dispose(); throw new Win32Exception(error); }
    }
    public void Add(Process process)
    {
        if (_handle != 0 && !AssignProcessToJobObject(_handle, process.Handle)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Dispose() { if (_handle != 0) { CloseHandle(_handle); _handle = 0; } }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    { public long ProcessUserTimeLimit, JobUserTimeLimit; public uint LimitFlags; public nuint MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit; public nuint Affinity; public uint PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
    { public BasicLimits BasicLimitInformation; public IoCounters IoInfo; public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateJobObject(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(nint job, int infoClass, ref ExtendedLimits info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(nint job, nint process);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
}
