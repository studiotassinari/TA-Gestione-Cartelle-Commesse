using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using Microsoft.Win32.SafeHandles;

namespace TA.Transport;

// Confronta il proprietario del pipe con il PID dichiarato dal gestore servizi Windows.
public static class LocalServiceIdentity
{
    public static void Verify(SafePipeHandle pipe)
    {
        var manager = OpenSCManager(null, null, 1); // SC_MANAGER_CONNECT
        if (manager == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var service = OpenService(manager, "TACommesseServer", 4); // SERVICE_QUERY_STATUS
            if (service == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if (!QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<Status>(), out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                if (status.State != 4 || status.ProcessId == 0) throw new AuthenticationException("TACommesseServer non risulta in esecuzione.");
                VerifyProcess(pipe, status.ProcessId);
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }
    public static void VerifyProcess(SafePipeHandle pipe, uint expected)
    {
        if (!GetNamedPipeServerProcessId(pipe, out var actual)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (expected == 0 || actual != expected) throw new AuthenticationException("Il canale locale non appartiene al servizio Windows atteso.");
    }
    [StructLayout(LayoutKind.Sequential)] private struct Status
    {
        public uint Type, State, Controls, ExitCode, ServiceExitCode, Checkpoint, WaitHint, ProcessId, Flags;
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenService(IntPtr manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceStatusEx(IntPtr service, int level, out Status status, int size, out int needed);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseServiceHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}
