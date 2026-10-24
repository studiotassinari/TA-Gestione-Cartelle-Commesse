// Importa i tipi fondamentali delle finestre WPF.
using System.Windows;
// Importa DataGridRow per riconoscere la riga su cui avviene il gesto.
using System.Windows.Controls;
// Importa i tipi degli eventi e dei pulsanti del mouse.
using System.Windows.Input;
// Importa i record di cartella utilizzati dalla vista.
using TA_Gestione_Cartelle_Commesse.Models;
// Importa i ViewModel utilizzati dalla vista.
using TA_Gestione_Cartelle_Commesse.ViewModels;

// Colloca la finestra nel namespace dell'applicazione.
namespace TA_Gestione_Cartelle_Commesse;

// Completa la classe parziale generata da WPF a partire da MainWindow.xaml.
public partial class MainWindow : Window
// Inizio del blocco di istruzioni o della definizione precedente.
{
    // Inizializza i controlli, le risorse e i binding dichiarati nel file XAML.
    private TA.Desktop.TrayLifetime? tray;
    public MainWindow()
    {
        InitializeComponent();
        tray = new TA.Desktop.TrayLifetime(this, "TA - Commesse", "Client");
        if (DataContext is MainViewModel model)
        {
            model.Commesse.DataChanged += message => tray.Notify("Commesse aggiornate", message);
            model.Gare.DataChanged += message => tray.Notify("Gare aggiornate", message);
        }
        Closed += (_, _) => { if (DataContext is MainViewModel viewModel) viewModel.Stop(); };
    }

    // Avvia il caricamento dell'archivio solo quando la vista è pronta a ricevere le notifiche.
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        TA.Desktop.UpdateLauncher.Check("Client");
        if (DataContext is MainViewModel viewModel) await viewModel.InitializeAsync();
    }

    // I gestori traducono i gesti della vista nel comando condiviso del ViewModel.
    // Seleziona la riga prima dell'apertura del suo menu contestuale.
    private void FolderRow_RightButtonDown(object sender, MouseButtonEventArgs e)
    // Inizio del blocco di istruzioni o della definizione precedente.
    {
        // Prosegue soltanto se l'evento proviene da una riga della tabella.
        if (sender is DataGridRow row)
        // Inizio del blocco di istruzioni o della definizione precedente.
        {
            // Rende corrente la riga su cui l'utente ha premuto il pulsante destro.
            row.IsSelected = true;
            // Sposta il focus sulla riga selezionata per allineare navigazione e menu contestuale.
            row.Focus();
        // Fine del blocco di istruzioni o della definizione corrente.
        }
    // Fine del blocco di istruzioni o della definizione corrente.
    }

    // Traduce il doppio clic sulla riga nel comando di apertura della categoria.
    private void FolderRow_DoubleClick(object sender, MouseButtonEventArgs e)
    // Inizio del blocco di istruzioni o della definizione precedente.
    {
        // Verifica che il doppio clic sia stato effettuato con il pulsante sinistro.
        if (e.ChangedButton != MouseButton.Left ||
            // Verifica inoltre che il mittente sia una riga della tabella.
            sender is not DataGridRow row ||
            // Verifica che il contesto della riga contenga una cartella riconosciuta dalla vista.
            row.DataContext is not FolderRecord record ||
            // Ricava dal Tag il ViewModel della categoria; in assenza di uno dei requisiti termina il gestore.
            row.Tag is not CategoryViewModel category) return;

        // Allinea la selezione del ViewModel alla cartella su cui è avvenuto il doppio clic.
        category.Selected = record;
        // Controlla che il comando di apertura sia abilitato prima di invocarlo.
        if (category.OpenFolderCommand.CanExecute(null))
            // Esegue lo stesso comando usato dai menu, senza duplicare la logica di apertura.
            category.OpenFolderCommand.Execute(null);
        // Segna l'evento come gestito per impedirne un'ulteriore elaborazione nella catena degli eventi.
        e.Handled = true;
    // Fine del blocco di istruzioni o della definizione corrente.
    }
// Fine del blocco di istruzioni o della definizione corrente.
}
