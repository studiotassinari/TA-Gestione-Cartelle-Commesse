using GeCo.Dati.Regole;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server.SharedDatabase;

// Lavoro quotidiano sulle commesse di GeCo (2.42.0): «Le mie commesse» e «Tutte» (ElencoCommesse), scheda della commessa con
// dati generali e fasi (ServizioCommessa: stesse regole di lettura e di modifica di GeCo2027, concorrenza con versione e lock),
// stato dell'accesso e richieste di accesso (ServizioRichiesteAccesso), cartella collegata (ServizioCollegamentiCartelle, sola
// lettura). Tutto con la libreria GeCo.Dati sull'utente del token Windows: chi vede, chi modifica e chi decide lo stabiliscono le
// regole, mai un campo della richiesta. Il contesto ha l'origine GeCa: i salvataggi registrano l'evento tecnico
// «CommessaAggiornata» (GeCo2027 e gli altri client ricaricano) e la notifica «CommessaModificata»; le richieste di accesso le
// notifiche mirate di sempre. Nessun importo: costi e ricavi restano in GeCo2027.
public sealed partial class GeCoWorkService
{
    public const string ProjectNotFound = "Nessuna commessa di GeCo con il codice {0} (la cartella può non essere ancora collegata a una commessa).";
    public static bool IsProject(string action) => action is "Projects" or "ProjectCard" or "SaveProject" or "AddPhase" or "SavePhase" or "RemovePhase"
        or "RequestAccess" or "AccessRequests" or "CancelAccess" or "DecideAccess" or "RevokeAccess";

    private static async Task<object> HandleProjectAsync(GeCoCommand c, ContestoRegole context, CancellationToken token)
    {
        var project = new ServizioCommessa(context);
        var access = new ServizioRichiesteAccesso(context, EventiSincronizzazione.GeCa);
        switch (c.Action)
        {
            case "Projects":
                return As<GeCoProjectList>(await ElencoCommesse.LeggiAsync(context, c.Valore, c.Testo, c.IdCdc, stop: token));
            case "ProjectCard":
                var id = c.IdCommessa > 0 ? c.IdCommessa : await ServizioTeamCommessa.CommessaDaCodiceAsync(context.Db, context.IdSocieta, c.Testo, token);
                if (id is not int found || found <= 0) return new GeCoProjectCardResult(false, string.Format(ProjectNotFound, (c.Testo ?? "").Trim()), null, null, null);
                return await CardAsync(context, found, token);
            case "SaveProject":
                var e = c.Commessa ?? throw new ArgumentException("Dati della commessa mancanti.");
                await project.ModificaDatiGeneraliAsync(c.IdCommessa, new DatiGeneraliCommessa(e.Versione, e.Descrizione, e.Note, e.IdResponsabile, e.IdRespContr,
                    e.IdManager, e.IdTipo, e.DataIniPrev, e.DataFinePrev, e.DataIniEff, e.DataFineEff), token);
                return await CardAsync(context, c.IdCommessa, token);
            case "AddPhase":
                await project.InserisciFaseAsync(c.IdCommessa, Phase(c.Fase), token);
                return await CardAsync(context, c.IdCommessa, token);
            case "SavePhase":
                // Da GeCa l'importo dei ricavi della fase non si mostra e quindi non si cambia (ricavi: false).
                await project.ModificaFaseAsync(c.Id, Phase(c.Fase), ricavi: false, token);
                return await CardAsync(context, c.IdCommessa, token);
            case "RemovePhase":
                await project.EliminaFaseAsync(c.Id, token);
                return await CardAsync(context, c.IdCommessa, token);
            case "RequestAccess":
                await access.RichiediAsync(c.IdCommessa, c.Testo, token);
                return await CardAsync(context, c.IdCommessa, token);
            case "AccessRequests": return await RequestsAsync(access, context, token);
            case "CancelAccess": await access.AnnullaAsync(c.Id, c.Versione, token); return await RequestsAsync(access, context, token);
            case "DecideAccess": await access.DecidiAsync(c.Id, c.Valore, c.Testo, c.Versione, token); return await RequestsAsync(access, context, token);
            default: await access.RevocaAsync(c.Id, c.Testo, c.Versione, token); return await RequestsAsync(access, context, token);
        }
    }

    private static DatiFase Phase(GeCoPhaseEdit? f) => f is null ? throw new ArgumentException("Fase mancante.")
        : new DatiFase(f.IdTipoFase, f.Descrizione, f.Ordinamento, f.IdResponsabile, null, f.Stato);

    private static async Task<GeCoAccessRequests> RequestsAsync(ServizioRichiesteAccesso access, ContestoRegole context, CancellationToken token) =>
        ServizioRichiesteAccesso.Disponibile(context.VersioneDatabase)
            ? As<GeCoAccessRequests>(await access.ElencoAsync(token))
            : throw new IOException("Richieste di accesso non disponibili: il database comune non è ancora aggiornato (migrazione 13). Aggiornare prima GeCo2027 o riavviare il servizio.");

    // Scheda completa: dati e fasi (regole di GeCo2027), stato dell'accesso e cartella. La cartella è la situazione del collegamento
    // calcolata dalla libreria, la stessa del riquadro «Cartella» della scheda di GeCo2027 (consultabile da tutti, nessun importo).
    private static async Task<GeCoProjectCardResult> CardAsync(ContestoRegole context, int id, CancellationToken token)
    {
        var card = As<GeCoProjectCard>(await new ServizioCommessa(context).SchedaAsync(id, token));
        GeCoAccessState? state = ServizioRichiesteAccesso.Disponibile(context.VersioneDatabase)
            ? As<GeCoAccessState>(await new ServizioRichiesteAccesso(context, EventiSincronizzazione.GeCa).StatoAsync(id, token)) : null;
        GeCoLinkRow? folder = null;
        if (context.VersioneDatabase >= EventiSincronizzazione.VersioneRichiesta)
        {
            var (row, all) = await new ServizioCollegamentiCartelle(context, EventiSincronizzazione.GeCa).PerElementoAsync(CollegamentiCartelle.Commessa, id, token);
            if (row != null) folder = Map(all with { Elementi = [row], CartelleLibere = [], Conflitti = [] }, []).Elementi.FirstOrDefault();
        }
        return new GeCoProjectCardResult(true, "", card, state, folder);
    }
}
