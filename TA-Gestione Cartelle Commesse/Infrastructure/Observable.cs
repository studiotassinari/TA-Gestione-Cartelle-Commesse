// Importa gli eventi di notifica delle proprietà utilizzati dai binding WPF.
using System.ComponentModel;
// Importa CallerMemberName per ricavare automaticamente il nome della proprietà.
using System.Runtime.CompilerServices;

// Raggruppa questo tipo nel livello Infrastructure dellʼapplicazione.
namespace TA_Gestione_Cartelle_Commesse.Infrastructure;

// Fornisce alle classi derivate la notifica dei cambiamenti richiesta dai binding WPF.
public abstract class Observable : INotifyPropertyChanged
// Inizio del blocco di istruzioni o della definizione precedente.
{
    // Espone l'evento ascoltato dalla vista quando cambia una proprietà.
    public event PropertyChangedEventHandler? PropertyChanged;
    // Notifica gli ascoltatori, usando il nome del chiamante se non specificato esplicitamente.
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
// Fine del blocco di istruzioni o della definizione corrente.
}
