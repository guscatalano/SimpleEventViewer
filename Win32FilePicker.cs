using System.Runtime.InteropServices;

namespace SimpleEventViewer_WinUI;

/// <summary>
/// Win32 IFileOpenDialog wrapper. The WinUI 3 FileOpenPicker is unreliable for
/// packaged apps run via dotnet run; this uses the underlying Windows API directly.
/// </summary>
internal static class Win32FilePicker
{
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileName(ref OpenFileName ofn);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string? lpstrFilter;
        public string? lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public string? lpstrFile;
        public int nMaxFile;
        public string? lpstrFileTitle;
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

    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_EXPLORER = 0x00080000;

    public static string? PickFile(IntPtr ownerHwnd, string title, string filterLabel, string extension)
    {
        var fileBuffer = new string('\0', 32768);

        // Win32 filter format: "Label\0*.ext\0All files\0*.*\0\0"
        var ext = extension.StartsWith('.') ? "*" + extension : "*." + extension;
        var filter = $"{filterLabel} ({ext})\0{ext}\0All files (*.*)\0*.*\0";

        var ofn = new OpenFileName
        {
            lStructSize = Marshal.SizeOf<OpenFileName>(),
            hwndOwner = ownerHwnd,
            lpstrFilter = filter,
            nFilterIndex = 1,
            lpstrFile = fileBuffer,
            nMaxFile = fileBuffer.Length,
            lpstrTitle = title,
            Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_EXPLORER
        };

        if (GetOpenFileName(ref ofn))
        {
            return ofn.lpstrFile?.TrimEnd('\0');
        }
        return null;
    }
}
