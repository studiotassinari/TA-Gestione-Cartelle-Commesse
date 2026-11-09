// Importa i tipi di configurazione presenti nel modello iniziale del progetto.
using System.Configuration;
// Importa i tipi per dati tabellari presenti nel modello iniziale del progetto.
using System.Data;
// Importa Application e gli altri tipi fondamentali WPF.
using System.Windows;

// Raggruppa la classe di avvio nel namespace dell'applicazione.
namespace TA_Gestione_Cartelle_Commesse
// Inizio del blocco di istruzioni o della definizione precedente.
{
    /// <summary>
    /// Classe di avvio associata alla definizione WPF in App.xaml.
    /// </summary>
    // Completa la classe generata da App.xaml, ereditando il ciclo di vita standard WPF.
    public partial class App : Application
    // Inizio del blocco di istruzioni o della definizione precedente.
    {
        private TA.Desktop.SessionInstance? instance;
        // Il processo di copia su richiesta non avvia finestre, connessioni o controlli aggiornamento.
        protected override async void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Length == 1 && e.Args[0] == "--geological-copy-worker")
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                await Services.GeologyDocumentCopy.RunWorkerAsync(); Shutdown(); return;
            }
            instance = new TA.Desktop.SessionInstance("Client");
            if (!instance.IsPrimary) { Shutdown(); return; }
            base.OnStartup(e);
            MainWindow = new MainWindow();
            instance.Listen(MainWindow);
            MainWindow.Show();
        }
        protected override void OnExit(ExitEventArgs e)
        {
            instance?.Dispose();
            base.OnExit(e);
        }
    // Fine del blocco di istruzioni o della definizione corrente.
    }

// Fine del blocco di istruzioni o della definizione corrente.
}
