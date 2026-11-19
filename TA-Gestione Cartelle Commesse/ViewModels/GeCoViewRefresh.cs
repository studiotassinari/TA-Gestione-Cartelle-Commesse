using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// Aggiornamento immediato delle finestre Pianificazione e Ore lavorate (2.39.0), come useAggiornamentiVista di GeCo2027: quando
// pianificazione o ore vengono salvate in GeCo2027 o in un'altra finestra di GeCa, il servizio avvisa con la notifica push
// «GeCoViste» e il client chiede gli eventi che il suo utente può vedere (ViewEvents). Qui si decide se ricaricare: solo se
// l'evento tocca persone e giorni mostrati e non è un salvataggio di questa stessa finestra; più eventi ravvicinati danno una
// sola rilettura; mentre l'utente trascina, scrive o salva si aspetta, così non perde ciò che sta facendo.
public sealed class GeCoViewRefresh(string window, Func<bool> busy, Func<Task> reload, CancellationToken token)
{
    public const string Planning = "PianificazioneModificata", Hours = "OreModificate";
    public static TimeSpan Quiet { get; set; } = TimeSpan.FromMilliseconds(400);
    public static TimeSpan Retry { get; set; } = TimeSpan.FromSeconds(1);
    private GeCoViewUpdate? last;
    private bool pending, running;
    public bool Pending => pending;
    // Testo dell'avviso discreto dopo una ricarica.
    public event Action<string>? Refreshed;

    public static bool Own(GeCoViewUpdate u, string window) => u.Origine == "GeCa" && u.Scheda == window;
    // Le date viaggiano come aaaa-mm-gg: il confronto tra testi è quello tra date.
    public static bool Touches(GeCoViewUpdate u, IEnumerable<int> people, string from, string to) =>
        people.Contains(u.IdCdc) && string.CompareOrdinal(u.Dal, to) <= 0 && string.CompareOrdinal(u.Al, from) >= 0;
    public static string Text(GeCoViewUpdate? u, string? me) => u switch
    {
        null => "Dati aggiornati",
        { Origine: "GeCo2027" } => "Aggiornato da GeCo2027" + (string.IsNullOrWhiteSpace(u.Utente) ? "" : $" ({u.Utente})"),
        _ when !string.IsNullOrWhiteSpace(me) && u.Utente == me => "Aggiornato da un'altra finestra di GeCa",
        _ => "Aggiornato da un altro utente di GeCa" + (string.IsNullOrWhiteSpace(u.Utente) ? "" : $" ({u.Utente})"),
    };

    // Eventi arrivati: concerns dice se uno riguarda la vista (tipo, persone e giorni). Parziale: il client era rimasto troppo
    // indietro per sapere che cosa è cambiato, quindi si ricarica comunque.
    public void Arrived(GeCoViewUpdates updates, Func<GeCoViewUpdate, bool> concerns, string? me)
    {
        var hit = updates.Eventi.Where(u => !Own(u, window) && concerns(u)).LastOrDefault();
        if (hit == null && !updates.Parziale) return;
        last = hit ?? last; pending = true; this.me = me;
        if (!running) _ = RunAsync();
    }
    private string? me;
    // Ogni caricamento della vista (anche chiesto dall'utente) copre già gli eventi arrivati prima: niente rilettura in più.
    public void Loaded() => pending = false;

    public async Task RunAsync()
    {
        running = true;
        try
        {
            while (pending)
            {
                await Task.Delay(Quiet, token);
                while (busy()) await Task.Delay(Retry, token);
                if (!pending) break;
                var text = Text(last, me);
                pending = false; last = null;
                await reload();
                Refreshed?.Invoke(text);
            }
        }
        catch (OperationCanceledException) { }
        finally { running = false; }
    }
}
