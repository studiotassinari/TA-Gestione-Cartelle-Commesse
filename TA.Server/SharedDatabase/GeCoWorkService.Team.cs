using GeCo.Dati.Regole;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server.SharedDatabase;

// Team della commessa di GeCo (2.41.0): lettura e modifica con ServizioTeamCommessa della libreria, le stesse regole della
// sezione «Team» di GeCo2027. Chi può modificare (amministratori, team manager, capocommessa) lo decide la libreria sull'utente
// del token Windows, mai un campo della richiesta. Il contesto ha l'origine GeCa: ogni modifica registra l'evento tecnico
// «TeamModificato» (GeCo2027 e gli altri client si aggiornano) e la notifica mirata a chi entra nel team.
public sealed partial class GeCoWorkService
{
    public const string TeamNotFound = "Nessuna commessa di GeCo con il codice {0}: il team si gestisce sulle commesse di GeCo (la cartella può non essere ancora collegata).";
    public static bool IsTeam(string action) => action is "ProjectTeam" or "SetTeamMember" or "SetPermanentTeam";

    private static async Task<object> HandleTeamAsync(GeCoCommand c, ContestoRegole context, CancellationToken token)
    {
        var team = new ServizioTeamCommessa(context);
        switch (c.Action)
        {
            case "ProjectTeam":
                var id = c.IdCommessa > 0 ? c.IdCommessa : await team.CommessaDaCodiceAsync(c.Testo, token);
                if (id is not int project || project <= 0) return new GeCoProjectTeamResult(false, string.Format(TeamNotFound, (c.Testo ?? "").Trim()), null);
                return new GeCoProjectTeamResult(true, "", As<GeCoProjectTeam>(await team.LeggiAsync(project, token)));
            case "SetTeamMember": return As<GeCoProjectTeam>(await team.PersonaAsync(c.IdCommessa, c.IdCdc ?? 0, c.Valore, token));
            default: return As<GeCoProjectTeam>(await team.TeamPermanenteAsync(c.IdCommessa, c.Id, c.Valore, token));
        }
    }
}
