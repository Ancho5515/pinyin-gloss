using System.Runtime.InteropServices;

namespace PinyinGloss.CandidateReader;

// 接口顺序依据 LinuxA11y/IAccessible2 api/AccessibleText.idl；只调用读取成员。
// Copyright (c) 2007, 2013 Linux Foundation; 2006 IBM Corporation; 2000, 2006 Sun Microsystems, Inc.
// BSD 许可完整文本见 third-party/IAccessible2-LICENSE.txt。
[ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAccessibleServiceProvider
{ [PreserveSig] int QueryService(ref Guid service, ref Guid iid, out nint result); }

[ComImport, Guid("24FD2FFB-3AAD-4A08-8335-A3AD89C0FB4B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IIa2Text
{
    [PreserveSig] int AddSelection(int start, int end);
    [PreserveSig] int GetAttributes(int offset, out int start, out int end, [MarshalAs(UnmanagedType.BStr)] out string attributes);
    [PreserveSig] int GetCaretOffset(out int offset);
    [PreserveSig] int GetCharacterExtents(int offset, int coordinates, out int x, out int y, out int width, out int height);
    [PreserveSig] int GetSelectionCount(out int count);
    [PreserveSig] int GetOffsetAtPoint(int x, int y, int coordinates, out int offset);
    [PreserveSig] int GetSelection(int index, out int start, out int end);
    [PreserveSig] int GetText(int start, int end, [MarshalAs(UnmanagedType.BStr)] out string text);
    [PreserveSig] int GetTextBeforeOffset(int offset, int boundary, out int start, out int end, [MarshalAs(UnmanagedType.BStr)] out string text);
    [PreserveSig] int GetTextAfterOffset(int offset, int boundary, out int start, out int end, [MarshalAs(UnmanagedType.BStr)] out string text);
    [PreserveSig] int GetTextAtOffset(int offset, int boundary, out int start, out int end, [MarshalAs(UnmanagedType.BStr)] out string text);
    [PreserveSig] int RemoveSelection(int index);
    [PreserveSig] int SetCaretOffset(int offset);
    [PreserveSig] int SetSelection(int index, int start, int end);
    [PreserveSig] int GetCharacterCount(out int count);
    [PreserveSig] int ScrollSubstringTo(int start, int end, int scroll);
    [PreserveSig] int ScrollSubstringToPoint(int start, int end, int coordinates, int x, int y);
    [PreserveSig] int GetNewText(out Ia2TextSegment text);
    [PreserveSig] int GetOldText(out Ia2TextSegment text);
}
[StructLayout(LayoutKind.Sequential)]
internal struct Ia2TextSegment { [MarshalAs(UnmanagedType.BStr)] public string Text; public int Start, End; }
