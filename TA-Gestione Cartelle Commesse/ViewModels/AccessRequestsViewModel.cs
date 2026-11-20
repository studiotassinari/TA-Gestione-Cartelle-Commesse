using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// «Richieste di accesso alle commesse» (2.42.0), come la pagina RichiesteAccesso di GeCo2027: le richieste dell'utente (con
// «Annulla» finché sono in attesa) e, per chi decide (amministratori, capocommessa della commessa, team manager), quelle da
// decidere, gli accessi in vigore da revocare e le ultime decise. Chi decide che cosa lo stabilisce il servizio con
// ServizioRichiesteAccesso; ogni passaggio arriva come notifica mirata a chi interessa, in GeCo2027 e in GeCa.
public sealed class AccessRequestsViewModel(IGeCoGateway gateway) : IDisposable
{
    private readonly WindowGeCoGateway channel = new(gateway);
    private readonly CancellationTokenSource lifetime = new();
    public GeCoAccessRequests? Requests { get; private set; }
    public string Error { get; private set; } = "";
    public string Done { get; private set; } = "";
    public bool Busy { get; private set; }
    public event Action? Updated;

    public async Task LoadAsync() => await RunAsync(new("AccessRequests"), "");
    public Task CancelAsync(GeCoAccessRequest r) => RunAsync(new("CancelAccess", Id: r.Id, Versione: r.Versione), $"Richiesta per la commessa {r.Codice} annullata.");
    public Task DecideAsync(GeCoAccessRequest r, bool grant, string? note) => RunAsync(new("DecideAccess", Id: r.Id, Valore: grant, Testo: note, Versione: r.Versione),
        grant ? $"Accesso alla commessa {r.Codice} concesso a {r.Utente}." : $"Richiesta di {r.Utente} per la commessa {r.Codice} rifiutata.");
    public Task RevokeAsync(GeCoAccessRequest r, string? note) => RunAsync(new("RevokeAccess", Id: r.Id, Testo: note, Versione: r.Versione), $"Accesso di {r.Utente} alla commessa {r.Codice} revocato.");

    // Dopo un rifiuto (richiesta cambiata nel frattempo, permessi) si rilegge l'elenco vero, tenendo il messaggio.
    private async Task RunAsync(GeCoCommand command, string done)
    {
        if (Busy) return;
        Busy = true; Updated?.Invoke();
        bool reload = false;
        try { Requests = await channel.Send<GeCoAccessRequests>(command, lifetime.Token); Error = ""; Done = done; }
        catch (Exception ex) when (ex is not OperationCanceledException) { Error = ProjectCardViewModel.Text(ex); Done = ""; reload = command.Action != "AccessRequests"; }
        finally { Busy = false; Updated?.Invoke(); }
        if (reload)
        {
            var error = Error;
            try { Requests = await channel.Send<GeCoAccessRequests>(new("AccessRequests"), lifetime.Token); } catch (Exception ex) when (ex is not OperationCanceledException) { }
            Error = error; Updated?.Invoke();
        }
    }

    public static string Line(GeCoAccessRequest r) =>
        $"{r.Codice}{(string.IsNullOrWhiteSpace(r.Descrizione) ? "" : " – " + r.Descrizione)} · {r.Utente} · {r.StatoTesto} · {r.DataRichiesta:dd/MM/yyyy HH:mm}";

    public void Dispose() { lifetime.Cancel(); lifetime.Dispose(); }
}
