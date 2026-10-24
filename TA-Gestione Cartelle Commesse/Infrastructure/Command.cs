// Importa il contratto ICommand dei comandi WPF.
using System.Windows.Input;

// Raggruppa questo tipo nel livello Infrastructure dellʼapplicazione.
namespace TA_Gestione_Cartelle_Commesse.Infrastructure;

// Adatta un'azione e una condizione facoltativa al contratto ICommand.
public sealed class Command(Action execute, Func<bool>? canExecute = null) : ICommand
// Inizio del blocco di istruzioni o della definizione precedente.
{
    // Segnala alla vista quando deve rivalutare l'abilitazione del comando.
    public event EventHandler? CanExecuteChanged;
    // Consente l'esecuzione se la condizione è vera; senza condizione il comando è abilitato.
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    // Ricontrolla l'abilitazione prima di invocare l'azione associata.
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(); }
    // Richiede ai controlli collegati di aggiornare il proprio stato abilitato/disabilitato.
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
// Fine del blocco di istruzioni o della definizione corrente.
}
