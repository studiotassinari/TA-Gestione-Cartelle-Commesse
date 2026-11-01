using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TA.Server.Documents;

// Apertura esclusivamente in lettura; controlla anche la destinazione reale dell'handle Windows.
public static class SafeDocumentSource
{
    public static string Resolve(string allowedRoot,string folderRoot,string relative)
    {
        TA_Gestione_Cartelle_Commesse.Contracts.DocumentValidation.Relative(relative);
        var root=Path.GetFullPath(allowedRoot).TrimEnd(Path.DirectorySeparatorChar);
        var folder=Path.GetFullPath(folderRoot).TrimEnd(Path.DirectorySeparatorChar);
        if(!Inside(folder,root))throw new UnauthorizedAccessException("Cartella fuori dalle sorgenti configurate.");
        var path=Path.GetFullPath(Path.Combine(folder,relative.Replace('/',Path.DirectorySeparatorChar)));
        if(!Inside(path,folder))throw new UnauthorizedAccessException("Percorso fuori dalla cartella selezionata.");
        CheckComponents(root,path);return path;
    }
    private static bool Inside(string path,string root)=>path.Equals(root,StringComparison.OrdinalIgnoreCase)||path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    private static void CheckComponents(string root,string path)
    {
        var current=path;
        while(true)
        {
            if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new UnauthorizedAccessException("Collegamenti simbolici e punti di reindirizzamento esclusi.");
            if(current.Equals(root,StringComparison.OrdinalIgnoreCase))break;
            current=Path.GetDirectoryName(current)??throw new UnauthorizedAccessException("Radice non valida.");
        }
    }
    public static FileStream Open(string allowedRoot,string path)
    {
        var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        try{using var root=DirectoryHandle(allowedRoot);Verify(root,file.SafeFileHandle);return file;}catch{file.Dispose();throw;}
    }
    public static SafeFileHandle OpenDirectory(string allowedRoot,string path)
    {
        var handle=DirectoryHandle(path);
        try{using var root=DirectoryHandle(allowedRoot);Verify(root,handle);return handle;}catch{handle.Dispose();throw;}
    }
    private static SafeFileHandle DirectoryHandle(string path)
    {
        // Nessun accesso scrittura. Non condivide DELETE mentre la directory viene enumerata.
        var handle=CreateFile(path,0,3,IntPtr.Zero,3,0x02000000,IntPtr.Zero);
        if(handle.IsInvalid){handle.Dispose();throw new Win32Exception(Marshal.GetLastWin32Error());}return handle;
    }
    private static void Verify(SafeFileHandle root,SafeFileHandle file)
    {if(!Inside(Final(file),Final(root).TrimEnd('\\')))throw new UnauthorizedAccessException("Il percorso reale esce dalla sorgente autorizzata.");}
    private static string Final(SafeFileHandle handle)
    {var text=new StringBuilder(32768);var count=GetFinalPathNameByHandle(handle,text,(uint)text.Capacity,0);if(count==0||count>=text.Capacity)throw new Win32Exception(Marshal.GetLastWin32Error());return text.ToString();}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle,StringBuilder path,uint size,uint flags);
}
