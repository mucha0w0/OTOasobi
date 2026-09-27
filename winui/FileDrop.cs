using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Otoasobi;

static class FileDrop
{
    const ushort CF_HDROP = 15;
    const uint TYMED_HGLOBAL = 1;
    const uint DVASPECT_CONTENT = 1;
    const uint DROPEFFECT_COPY = 1;
    const uint DROPEFFECT_NONE = 0;
    const int S_OK = 0;
    const int DRAGDROP_E_ALREADYREGISTERED = unchecked((int)0x80040101);

    static readonly List<FileDropTarget> Targets = [];
    static Action<IReadOnlyList<string>>? _onFiles;

    public static void Attach(Window window, Action<IReadOnlyList<string>> onFiles)
    {
        _onFiles = onFiles;
        OleInitialize(IntPtr.Zero);
        var hwnd = WindowNative.GetWindowHandle(window);
        HookTree(hwnd);
        window.Activated += (_, _) => HookTree(WindowNative.GetWindowHandle(window));
    }

    static void HookTree(IntPtr hwnd)
    {
        Register(hwnd);
        EnumChildWindows(hwnd, (child, _) =>
        {
            Register(child);
            return true;
        }, IntPtr.Zero);
    }

    static void Register(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || Targets.Any(item => item.Hwnd == hwnd))
            return;
        var target = new FileDropTarget(hwnd, paths => _onFiles?.Invoke(paths));
        var hr = RegisterDragDrop(hwnd, target);
        if (hr == S_OK || hr == DRAGDROP_E_ALREADYREGISTERED)
            Targets.Add(target);
    }

    sealed class FileDropTarget : IDropTarget
    {
        readonly Action<IReadOnlyList<string>> _onFiles;

        public FileDropTarget(IntPtr hwnd, Action<IReadOnlyList<string>> onFiles)
        {
            Hwnd = hwnd;
            _onFiles = onFiles;
        }

        public IntPtr Hwnd { get; }

        public int DragEnter(IntPtr dataObject, uint keyState, POINTL point, ref uint effect)
        {
            effect = HasFiles(dataObject) ? DROPEFFECT_COPY : DROPEFFECT_NONE;
            return S_OK;
        }

        public int DragOver(uint keyState, POINTL point, ref uint effect)
        {
            effect = DROPEFFECT_COPY;
            return S_OK;
        }

        public int DragLeave() => S_OK;

        public int Drop(IntPtr dataObject, uint keyState, POINTL point, ref uint effect)
        {
            effect = DROPEFFECT_COPY;
            var files = ReadFiles(dataObject);
            if (files.Count > 0)
                _onFiles(files);
            return S_OK;
        }
    }

    static bool HasFiles(IntPtr dataObject)
    {
        if (dataObject == IntPtr.Zero)
            return false;
        var data = (IDataObject)Marshal.GetObjectForIUnknown(dataObject);
        var format = HdropFormat();
        return data.QueryGetData(ref format) == S_OK;
    }

    static List<string> ReadFiles(IntPtr dataObject)
    {
        var files = new List<string>();
        if (dataObject == IntPtr.Zero)
            return files;
        var data = (IDataObject)Marshal.GetObjectForIUnknown(dataObject);
        var format = HdropFormat();
        if (data.GetData(ref format, out var medium) != S_OK)
            return files;
        try
        {
            var count = DragQueryFile(medium.unionmember, 0xFFFFFFFF, null, 0);
            var buffer = new StringBuilder(2048);
            for (uint i = 0; i < count; i++)
            {
                buffer.Clear();
                if (DragQueryFile(medium.unionmember, i, buffer, buffer.Capacity) > 0)
                    files.Add(buffer.ToString());
            }
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }

        return files;
    }

    static FORMATETC HdropFormat()
    {
        return new FORMATETC
        {
            cfFormat = CF_HDROP,
            ptd = IntPtr.Zero,
            dwAspect = DVASPECT_CONTENT,
            lindex = -1,
            tymed = TYMED_HGLOBAL,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINTL
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct FORMATETC
    {
        public ushort cfFormat;
        public IntPtr ptd;
        public uint dwAspect;
        public int lindex;
        public uint tymed;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct STGMEDIUM
    {
        public uint tymed;
        public IntPtr unionmember;
        public IntPtr pUnkForRelease;
    }

    [ComImport]
    [Guid("0000010E-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDataObject
    {
        [PreserveSig]
        int GetData(ref FORMATETC format, out STGMEDIUM medium);

        [PreserveSig]
        int GetDataHere(ref FORMATETC format, ref STGMEDIUM medium);

        [PreserveSig]
        int QueryGetData(ref FORMATETC format);

        [PreserveSig]
        int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut);

        [PreserveSig]
        int SetData(ref FORMATETC format, ref STGMEDIUM medium, [MarshalAs(UnmanagedType.Bool)] bool release);

        [PreserveSig]
        int EnumFormatEtc(uint direction, out IntPtr enumerator);

        [PreserveSig]
        int DAdvise(ref FORMATETC format, uint advf, IntPtr sink, out uint connection);

        [PreserveSig]
        int DUnadvise(uint connection);

        [PreserveSig]
        int EnumDAdvise(out IntPtr enumerator);
    }

    [ComImport]
    [Guid("00000122-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDropTarget
    {
        [PreserveSig]
        int DragEnter(IntPtr dataObject, uint keyState, POINTL point, ref uint effect);

        [PreserveSig]
        int DragOver(uint keyState, POINTL point, ref uint effect);

        [PreserveSig]
        int DragLeave();

        [PreserveSig]
        int Drop(IntPtr dataObject, uint keyState, POINTL point, ref uint effect);
    }

    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("ole32.dll")]
    static extern int OleInitialize(IntPtr reserved);

    [DllImport("ole32.dll")]
    static extern int RegisterDragDrop(IntPtr hwnd, IDropTarget dropTarget);

    [DllImport("ole32.dll")]
    static extern void ReleaseStgMedium(ref STGMEDIUM medium);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern uint DragQueryFile(IntPtr hDrop, uint index, StringBuilder? file, int size);

    [DllImport("user32.dll")]
    static extern bool EnumChildWindows(IntPtr hwnd, EnumWindowsProc callback, IntPtr lParam);
}
