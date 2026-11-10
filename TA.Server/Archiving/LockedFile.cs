using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TA.Server.Archiving;

// Gli handle impediscono sostituzioni/rinomine durante il confronto. La cancellazione
// usa l'handle già verificato, non riapre un percorso che potrebbe indicare un altro file.
internal static class LockedFile
{
    public static SafeFileHandle Open(string path, bool directory, bool delete)
    {
        var handle = CreateFileW(path, (directory ? 0x80u : 0x80000000u) | (delete ? 0x10000u : 0),
            1u, IntPtr.Zero, 3, 0x02000000u | 0x00200000u, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw Error(path); }
        try
        {
            if (!GetFileInformationByHandle(handle, out var info)) throw Error(path);
            if ((info.Attributes & ((uint)FileAttributes.ReparsePoint | (uint)FileAttributes.Encrypted)) != 0 ||
                (!directory && info.Links != 1) || ((info.Attributes & 16) != 0) != directory)
                throw new IOException("Collegamento, file cifrato o tipo non supportato: " + path);
            CheckStreams(path);
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    public static void Delete(SafeFileHandle handle)
    {
        // FILE_DISPOSITION_INFO contiene un BOOLEAN (un byte), non un BOOL Win32.
        byte delete = 1;
        if (!SetFileInformationByHandle(handle, 4, ref delete, 1)) throw Error("Eliminazione dell'elemento verificato");
    }

    private static void CheckStreams(string path)
    {
        var search = FindFirstStreamW(path, 0, out var stream, 0);
        if (search == new IntPtr(-1))
        {
            if (Marshal.GetLastWin32Error() == 38) return; // Nessun flusso nella directory.
            throw Error("Impossibile verificare tutti i flussi: " + path);
        }
        try
        {
            do
            {
                if (stream.Name != "::$DATA" && !SystemStream(stream)) throw new IOException("Flusso alternativo non rappresentabile nello ZIP: " + path);
            } while (FindNextStreamW(search, out stream));
            if (Marshal.GetLastWin32Error() != 38) throw Error(path);
        }
        finally { FindClose(search); }
    }

    // Flussi che Windows aggiunge da solo e che non contengono dati del documento: la marcatura di provenienza
    // dei file scaricati (pochi byte) e il segnaposto vuoto di Thumbs.db. Non entrano nello ZIP e non bloccano l'archiviazione;
    // qualunque altro flusso alternativo continua a impedirla, perché andrebbe perso con l'eliminazione dell'originale.
    private static bool SystemStream(StreamInfo stream) =>
        stream.Name.Equals(":Zone.Identifier:$DATA", StringComparison.OrdinalIgnoreCase) && stream.Size <= 4096 ||
        stream.Name.Equals(":encryptable:$DATA", StringComparison.OrdinalIgnoreCase) && stream.Size == 0;
    private static IOException Error(string path) => new(path + ": " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
    [StructLayout(LayoutKind.Sequential)] private struct FileInfo
    {
        public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StreamInfo
    { public long Size; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)] public string Name; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfo info);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int type, ref byte info, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstStreamW(string path, int level, out StreamInfo info, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextStreamW(IntPtr search, out StreamInfo info);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FindClose(IntPtr search);
}
