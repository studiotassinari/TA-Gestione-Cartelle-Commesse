using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using System.Security;
namespace TA_Gestione_Cartelle_Commesse.Services;

// La password dell'account alternativo resta in Gestione credenziali di Windows, protetta dal profilo
// dell'utente Windows che l'ha salvata: mai in connection.json, nei log o nel repository.
// L'utente può vederla o eliminarla da Pannello di controllo > Gestione credenziali > Credenziali di Windows generiche.
public static class SavedCredential
{
    private const int Generic = 1, LocalMachine = 2, NotFound = 1168;
    private static string Target(string host) => "TA-Gestione Cartelle Commesse:" + host.Trim().ToLowerInvariant();

    public static NetworkCredential? Read(string host)
    {
        if (!CredRead(Target(host), Generic, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NotFound) return null;
            throw new Win32Exception(error, "Lettura credenziale memorizzata non riuscita");
        }
        try
        {
            var native = Marshal.PtrToStructure<Native>(pointer);
            var name = native.UserName ?? "";
            if (name.Length == 0 || native.BlobSize == 0 || native.Blob == IntPtr.Zero) return null;
            var password = Marshal.PtrToStringUni(native.Blob, (int)native.BlobSize / 2);
            return Credential(name, password);
        }
        finally { CredFree(pointer); }
    }

    public static void Save(string host, NetworkCredential credential)
    {
        var name = string.IsNullOrEmpty(credential.Domain) ? credential.UserName : credential.Domain + "\\" + credential.UserName;
        var secret = credential.SecurePassword;
        // Copia temporanea non gestita della password, azzerata subito dopo la scrittura.
        var blob = Marshal.SecureStringToGlobalAllocUnicode(secret);
        try
        {
            var native = new Native { Type = Generic, TargetName = Target(host), UserName = name, Persist = LocalMachine, BlobSize = (uint)(secret.Length * 2), Blob = blob,
                Comment = "Accesso al servizio TA-Gestione Cartelle Commesse con account di dominio diverso da quello Windows" };
            if (!CredWrite(ref native, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Memorizzazione credenziale non riuscita");
        }
        finally { Marshal.ZeroFreeGlobalAllocUnicode(blob); }
    }

    public static void Delete(string host)
    {
        if (!CredDelete(Target(host), Generic, 0) && Marshal.GetLastWin32Error() is var error && error != NotFound)
            throw new Win32Exception(error, "Eliminazione credenziale memorizzata non riuscita");
    }

    // Accetta DOMINIO\utente oppure utente@dominio, come la finestra di accesso.
    public static NetworkCredential Credential(string name, string password)
    {
        var parts = name.Split('\\', 2);
        return parts.Length == 2 ? new NetworkCredential(parts[1], password, parts[0]) : new NetworkCredential(name, password);
    }
    public static NetworkCredential Credential(string name, SecureString password)
    {
        var parts = name.Split('\\', 2);
        return parts.Length == 2 ? new NetworkCredential(parts[1], password, parts[0]) : new NetworkCredential(name, password);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Native
    {
        public uint Flags, Type; public string TargetName; public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint BlobSize; public IntPtr Blob; public uint Persist, AttributeCount; public IntPtr Attributes;
        public string? TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Native credential, int flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr buffer);
}
