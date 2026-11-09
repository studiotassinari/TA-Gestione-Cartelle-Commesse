using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TA.Desktop;

// Oggetti solo in memoria, condivisi fra versioni ma limitati alla sessione desktop Windows.
public sealed class SessionInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle activation;
    private RegisteredWaitHandle? listener;
    private bool disposed;
    public bool IsPrimary { get; }

    public SessionInstance(string component)
    {
        var name = @"Local\Tassinari.Commesse." + component;
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Activate");
        mutex = new Mutex(false, name + ".Instance");
        try { IsPrimary = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
        if (!IsPrimary)
        {
            // Il processo avviato dall'utente consente all'istanza esistente di prendere il primo piano.
            AllowSetForegroundWindow(-1);
            activation.Set();
        }
    }

    public void Listen(Window window)
    {
        if (!IsPrimary || listener != null) return;
        // AutoReset conserva anche una richiesta arrivata prima della creazione della finestra.
        listener = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) =>
        {
            if (window.Dispatcher.HasShutdownStarted) return;
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!disposed) Restore(window);
            }));
        }, null, Timeout.Infinite, false);
    }

    public static void Restore(Window window)
    {
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        SetForegroundWindow(new WindowInteropHelper(window).Handle);
    }

    // Chiamato sul thread di avvio: il mutex resta occupato anche quando la finestra è nascosta.
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        listener?.Unregister(null);
        if (IsPrimary) mutex.ReleaseMutex();
        mutex.Dispose();
        activation.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
}
