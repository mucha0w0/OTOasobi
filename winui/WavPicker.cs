using System.Runtime.InteropServices;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Otoasobi;

static class WavPicker
{
    const uint FosPickFolders = 0x00000020;
    const uint FosForceFileSystem = 0x00000040;
    const uint FosPathMustExist = 0x00000800;
    const uint SigdnFileSystemPath = 0x80058000;
    const int ErrorCancelled = unchecked((int)0x800704C7);

    public static async Task<string?> PickAsync(IntPtr hwnd)
    {
        try
        {
            var picker = new FolderPicker();
            InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeFilter.Add("*");
            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }
        catch (COMException)
        {
            return PickWin32(hwnd);
        }
    }

    static string? PickWin32(IntPtr hwnd)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialog();
        dialog.GetOptions(out var options);
        dialog.SetOptions(options | FosPickFolders | FosForceFileSystem | FosPathMustExist);
        dialog.SetTitle("音声フォルダを選択");
        var hr = dialog.Show(hwnd);
        if (hr == ErrorCancelled)
            return null;
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
        dialog.GetResult(out var item);
        item.GetDisplayName(SigdnFileSystemPath, out var path);
        return path;
    }

    [ComImport]
    [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    class FileOpenDialog
    {
    }

    [ComImport]
    [Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileOpenDialog
    {
        [PreserveSig]
        int Show(IntPtr parent);

        void SetFileTypes(uint count, IntPtr filters);

        void SetFileTypeIndex(uint index);

        void GetFileTypeIndex(out uint index);

        void Advise(IntPtr events, out uint cookie);

        void Unadvise(uint cookie);

        void SetOptions(uint options);

        void GetOptions(out uint options);

        void SetDefaultFolder(IShellItem item);

        void SetFolder(IShellItem item);

        void GetFolder(out IShellItem item);

        void GetCurrentSelection(out IShellItem item);

        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);

        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);

        void GetResult(out IShellItem item);

        void AddPlace(IShellItem item, uint placement);

        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);

        void Close(int hr);

        void SetClientGuid(ref Guid guid);

        void ClearClientData();

        void SetFilter(IntPtr filter);

        void GetResults(out IntPtr items);

        void GetSelectedItems(out IntPtr items);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr bindCtx, ref Guid handler, ref Guid iid, out IntPtr result);

        void GetParent(out IShellItem parent);

        void GetDisplayName(uint nameType, [MarshalAs(UnmanagedType.LPWStr)] out string name);

        void GetAttributes(uint mask, out uint attributes);

        void Compare(IShellItem other, uint hint, out int order);
    }
}
