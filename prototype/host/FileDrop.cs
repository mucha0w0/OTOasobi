using System.Runtime.InteropServices;
using System.Text;

namespace Otoasobi.Probe;

sealed class FileDrop : IDisposable
{
    readonly Action<string> _open;
    readonly List<IntPtr> _windows = [];
    bool _disposed;

    FileDrop(Action<string> open) => _open = open;

    public static FileDrop Attach(IntPtr owner, Action<string> open)
    {
        var drop = new FileDrop(open);
        drop.Register(owner);
        return drop;
    }

    public void Register(IntPtr owner)
    {
        if (_disposed || owner == IntPtr.Zero)
            return;
        TryRegister(owner);
        EnumChildWindows(owner, (hwnd, _) =>
        {
            TryRegister(hwnd);
            return true;
        }, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var hwnd in _windows)
            RevokeDragDrop(hwnd);
        _windows.Clear();
    }

    void TryRegister(IntPtr hwnd)
    {
        if (_windows.Contains(hwnd))
            return;
        RevokeDragDrop(hwnd);
        if (RegisterDragDrop(hwnd, new Target(_open)) == 0)
            _windows.Add(hwnd);
    }

    sealed class Target(Action<string> open) : IDropTarget
    {
        bool _accept;

        public int DragEnter(IntPtr data, uint key, PointL point, ref uint effect)
        {
            _accept = HasFile(data);
            effect = _accept ? 1u : 0u;
            return 0;
        }

        public int DragOver(uint key, PointL point, ref uint effect)
        {
            effect = _accept ? 1u : 0u;
            return 0;
        }

        public int DragLeave() => 0;

        public int Drop(IntPtr data, uint key, PointL point, ref uint effect)
        {
            effect = 0;
            var path = FirstZip(data);
            if (path != null)
            {
                effect = 1;
                open(path);
            }

            return 0;
        }

        static bool HasFile(IntPtr data) => FirstZip(data) != null;
    }

    static string? FirstZip(IntPtr data)
    {
        if (data == IntPtr.Zero)
            return null;
        var obj = (IDataObject)Marshal.GetObjectForIUnknown(data);
        try
        {
            return ReadZip(obj);
        }
        finally
        {
            Marshal.ReleaseComObject(obj);
        }
    }

    static string? ReadZip(IDataObject obj)
    {
        var format = new FormatEtc { Format = 15, Aspect = 1, Index = -1, Tymed = 1 };
        if (obj.QueryGetData(ref format) < 0)
            return null;
        if (obj.GetData(ref format, out var medium) < 0 || medium.Data == IntPtr.Zero)
            return null;
        try
        {
            var count = DragQueryFile(medium.Data, 0xFFFFFFFF, null, 0);
            string? zip = null;
            var found = 0;
            for (uint i = 0; i < count; i++)
            {
                var length = (int)DragQueryFile(medium.Data, i, null, 0);
                var builder = new StringBuilder(length + 1);
                DragQueryFile(medium.Data, i, builder, builder.Capacity);
                var path = builder.ToString();
                if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    continue;
                found++;
                zip = path;
            }

            return found == 1 ? zip : null;
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PointL
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct FormatEtc
    {
        public ushort Format;
        public IntPtr Device;
        public uint Aspect;
        public int Index;
        public uint Tymed;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct StgMedium
    {
        public uint Tymed;
        public IntPtr Data;
        public IntPtr Unknown;
    }

    [ComImport]
    [Guid("00000122-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDropTarget
    {
        [PreserveSig]
        int DragEnter(IntPtr data, uint key, PointL point, ref uint effect);

        [PreserveSig]
        int DragOver(uint key, PointL point, ref uint effect);

        [PreserveSig]
        int DragLeave();

        [PreserveSig]
        int Drop(IntPtr data, uint key, PointL point, ref uint effect);
    }

    [ComImport]
    [Guid("0000010e-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDataObject
    {
        [PreserveSig]
        int GetData(ref FormatEtc format, out StgMedium medium);

        [PreserveSig]
        int GetDataHere(ref FormatEtc format, ref StgMedium medium);

        [PreserveSig]
        int QueryGetData(ref FormatEtc format);

        [PreserveSig]
        int GetCanonicalFormatEtc(ref FormatEtc format, out FormatEtc canonical);

        [PreserveSig]
        int SetData(ref FormatEtc format, ref StgMedium medium, [MarshalAs(UnmanagedType.Bool)] bool release);

        [PreserveSig]
        int EnumFormatEtc(uint direction, out IntPtr enumerator);

        [PreserveSig]
        int DAdvise(ref FormatEtc format, uint advf, IntPtr sink, out uint connection);

        [PreserveSig]
        int DUnadvise(uint connection);

        [PreserveSig]
        int EnumDAdvise(out IntPtr enumerator);
    }

    delegate bool EnumProc(IntPtr hwnd, IntPtr param);

    [DllImport("user32.dll")]
    static extern bool EnumChildWindows(IntPtr hwnd, EnumProc proc, IntPtr param);

    [DllImport("ole32.dll")]
    static extern int RegisterDragDrop(IntPtr hwnd, IDropTarget target);

    [DllImport("ole32.dll")]
    static extern int RevokeDragDrop(IntPtr hwnd);

    [DllImport("ole32.dll")]
    static extern void ReleaseStgMedium(ref StgMedium medium);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern uint DragQueryFile(IntPtr drop, uint index, StringBuilder? file, int capacity);
}
