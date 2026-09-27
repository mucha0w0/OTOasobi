using System.Runtime.InteropServices;
using System.Text;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Otoasobi;

static class WavPicker
{
    public static async Task<string?> PickAsync(IntPtr hwnd)
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeFilter.Add(".wav");
            var file = await picker.PickSingleFileAsync();
            if (file != null)
                return file.Path;
            return null;
        }
        catch (COMException)
        {
            return PickWin32(hwnd);
        }
    }

    static string? PickWin32(IntPtr hwnd)
    {
        var buffer = new StringBuilder(4096);
        buffer.Append('\0');
        var ofn = new OpenFileName
        {
            lStructSize = Marshal.SizeOf<OpenFileName>(),
            hwndOwner = hwnd,
            lpstrFilter = "WAV\0*.wav\0",
            lpstrFile = buffer,
            nMaxFile = buffer.Capacity,
            lpstrTitle = "音声ファイルを選択",
            Flags = 0x00080000 | 0x00001000 | 0x00000800,
        };
        return GetOpenFileName(ref ofn) ? buffer.ToString() : null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string? lpstrFilter;
        public string? lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public StringBuilder? lpstrFile;
        public int nMaxFile;
        public StringBuilder? lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string? lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetOpenFileName(ref OpenFileName ofn);
}
