using System.Data.Common;
using System.Text.Json;
using GeCo.Dati;
using GeCo.Dati.Regole;
using Microsoft.EntityFrameworkCore;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server.SharedDatabase;

// Pianificazione delle attività, ore lavorate, collegamento delle cartelle (GeCoWorkService.Links.cs), allineamento
// (GeCoWorkService.Alignment.cs, 2.40.0) e team della commessa (GeCoWorkService.Team.cs, 2.41.0) di GeCo per il client
// WPF, sullo stesso database SQL Server di GeCo2027 e con le stesse regole: quelle dei servizi della libreria GeCo.Dati
// (ServizioPianificazione, ServizioConsuntivo, ServizioCollegamentiCartelle, ServizioAllineamento), mai riscritte qui.
// Il servizio decide soltanto chi è l'utente: l'account Windows autenticato sul canale (mai un nome inviato dal client)
// diventa un utente di GeCo con AccountWindows.TrovaUtenteAsync, la stessa funzione dell'accesso dal browser di GeCo2027.
// Senza utente corrispondente, o senza la società abilitata, nessun comando viene eseguito.
public sealed partial class GeCoWorkService
{
    private readonly Func<GeCoDbContext>? open;
    private readonly Func<CancellationToken, Task<int>> version;
    private readonly int companyId;
    private readonly Func<GeCoDbContext, int, int, Task<bool>> enabled;
    public GeCoWorkService(ServerSettings settings) : this(Open(settings.SharedDatabase), token => AggiornamentoDatabase.VersioneAttualeAsync(settings.SharedDatabase, token), settings.GeCoCompanyId) { }
    // open null = database comune non configurato. enabled: utente abilitato alla società (in GeCo2017 spSocieta_x_Utente).
    public GeCoWorkService(Func<GeCoDbContext>? open, Func<CancellationToken, Task<int>> version, int companyId = 0, Func<GeCoDbContext, int, int, Task<bool>>? enabled = null)
    { this.open = open; this.version = version; this.companyId = companyId; this.enabled = enabled ?? Enabled; }
    internal static Func<GeCoDbContext>? Open(string connection) => string.IsNullOrWhiteSpace(connection) ? null
        : () => new GeCoDbContext(new DbContextOptionsBuilder<GeCoDbContext>().UseSqlServer(connection).Options);
    // Stessa verifica dell'accesso a GeCo2027: la società di lavoro deve essere tra quelle dell'utente.
    private static async Task<bool> Enabled(GeCoDbContext db, int user, int company) => (await db.spSocieta_x_Utente(user)).Any(s => s.Soc_Id == company);

    public const string NotConfigured = "Il database comune con GeCo non è configurato sul server: pianificazione e ore lavorate non sono disponibili. Un responsabile autorizzato lo imposta in Gestore server > Configura.";

    public async Task<object> ExecuteAsync(GeCoCommand command, string account, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(account)) throw new UnauthorizedAccessException("Identità Windows mancante.");
        if (open == null) return command.Action == "Access" ? new GeCoAccess(false, NotConfigured, account) : throw new IOException(NotConfigured);
        try
        {
            await using var db = open();
            var current = await version(token);
            var (access, context) = await IdentifyAsync(db, account, current, command.Action == "Access", command.Scheda, token);
            if (command.Action == "Access") return access;
            if (context == null) throw new UnauthorizedAccessException(access.Message);
            return IsAlignment(command.Action) ? await HandleAlignmentAsync(command, context, token) : await HandleAsync(command, context, token);
        }
        catch (RegolaException ex) { throw Translate(ex); }
        catch (DbException ex) { throw new IOException("Database comune con GeCo non raggiungibile: " + ex.Message, ex); }
        catch (DbUpdateException ex) { throw new IOException("Salvataggio nel database comune non riuscito: " + (ex.InnerException ?? ex).Message, ex); }
        // EF segnala così i guasti di collegamento e le transazioni interrotte: senza risposta il client vedrebbe solo il canale chiuso.
        catch (InvalidOperationException ex) when (ex is not ObjectDisposedException) { throw new IOException("Operazione sul database comune non riuscita: " + ex.Message, ex); }
    }

    // Gli errori delle regole hanno il messaggio da mostrare e uno stato nello stile HTTP: diventano le eccezioni che TcpService
    // restituisce al client come rifiuto, con lo stesso messaggio di GeCo2027.
    public static Exception Translate(RegolaException ex) => ex.Stato switch
    {
        401 or 403 => new UnauthorizedAccessException(ex.Message, ex),
        404 => new KeyNotFoundException(ex.Message, ex),
        503 => new IOException(ex.Message, ex),
        _ => new ArgumentException(ex.Message, ex),
    };

    private async Task<(GeCoAccess Access, ContestoRegole? Context)> IdentifyAsync(GeCoDbContext db, string account, int current, bool register, string? window, CancellationToken token)
    {
        var found = await AccountWindows.TrovaUtenteAsync(db, account, current, null, token);
        // All'accesso (non a ogni comando) l'account finisce tra quelli visti, per il raccordo degli amministratori in GeCo2027.
        // La funzione della libreria non solleva mai eccezioni: se la scrittura non riesce l'accesso continua lo stesso.
        if (register) await AccountWindows.RegistraVistoAsync(db, account, AccountWindowsVisti.GeCa, current, found, null, token);
        if (found.Utente is not { } user)
            return (new(false, $"L'account Windows {account} non corrisponde a nessun utente attivo di GeCo: pianificazione e ore lavorate non sono disponibili. Un amministratore di GeCo può abbinarlo nella pagina Utenti.", account, DatabaseVersion: current), null);
        var company = await (companyId > 0 ? db.tblSocieta.AsNoTracking().Where(s => s.Soc_Id == companyId) : db.tblSocieta.AsNoTracking().OrderBy(s => s.Soc_Id)).FirstOrDefaultAsync(token);
        if (company == null)
            return (new(false, companyId > 0 ? $"La società {companyId} indicata nella configurazione del server non esiste nel database di GeCo." : "Nessuna società nel database di GeCo.", account, user.Ute_Username, DatabaseVersion: current), null);
        string name = string.IsNullOrWhiteSpace(user.Ute_Descrizione) ? user.Ute_Username : user.Ute_Descrizione;
        if (!await enabled(db, user.Ute_Id, company.Soc_Id))
            return (new(false, $"L'utente {user.Ute_Username} di GeCo non è abilitato alla società {company.Soc_RagSoc}.", account, user.Ute_Username, name, found.Come ?? "", company.Soc_RagSoc, current), null);
        // Origine GeCa: ogni salvataggio di pianificazione e ore registra l'evento tecnico che aggiorna subito GeCo2027 e gli altri
        // client di GeCa; la scheda è la finestra che ha salvato (la libreria scarta un valore non valido), che non si ricarica da sé.
        var context = new ContestoRegole(db, user, company.Soc_Id, company, current) { Origine = EventiSincronizzazione.GeCa, Scheda = EventiSincronizzazione.Scheda(window) };
        bool hours = await HasHoursAsync(db, user);
        bool planning = current >= Pianificazione.VersioneRichiesta;
        var message = planning ? "" : "Pianificazione non disponibile: il database non è stato aggiornato. Aggiornare prima GeCo2027 o riavviare il servizio.";
        return (new(true, message, account, user.Ute_Username, name, found.Come ?? "", company.Soc_RagSoc, current, planning, hours,
            Ruoli.Amministratore(user), current >= EventiSincronizzazione.VersioneRichiesta), context);
    }
    private static async Task<bool> HasHoursAsync(GeCoDbContext db, tblUtenti user)
    {
        try { await PermessiFunzioni.PerPaginaAsync(db, user, ServizioConsuntivo.Pagina); return true; }
        catch (RegolaException ex) when (ex.Stato == 403) { return false; }
    }

    // Comandi che salvano pianificazione, ore o team della commessa (e quindi registrano un evento tecnico): dopo di essi il lettore del registro
    // rilegge subito (GeCoEventHub.Wake), così gli altri client aperti si aggiornano senza aspettare il giro successivo.
    public static bool Saves(string action) => action is "AddActivity" or "MoveActivity" or "RemoveActivity" or "HoursSave" or "HoursConfirm" or "SetTeamMember" or "SetPermanentTeam";

    private static async Task<object> HandleAsync(GeCoCommand c, ContestoRegole context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var plan = new ServizioPianificazione(context);
        var hours = new ServizioConsuntivo(context);
        switch (c.Action)
        {
            case "Plan":
                var p = await plan.PianoAsync(string.IsNullOrWhiteSpace(c.Dal) ? null : ServizioPianificazione.LeggiData(c.Dal, "Inizio"), c.Settimane ?? 4, c.Persone);
                return new GeCoPlan(DateRegole.Iso(p.Dal), p.Settimane, DateRegole.Iso(p.Oggi), p.OreGiorno, p.Chi.NomeRuolo, As<GeCoPerson[]>(p.Elenco), As<GeCoShownPerson[]>(p.Persone),
                    p.PuoPianificare, p.PuoOrganizzare, As<GeCoPlanProject[]>(p.Commesse), As<GeCoActivity[]>(p.Attivita));
            case "PlanProjects": return As<GeCoPlanProject[]>(await plan.CercaCommesseAsync(c.Testo));
            case "AddActivity":
                var a = c.Attivita ?? throw new ArgumentException("Attività mancante.");
                return await plan.AggiungiAsync(new AttivitaRichiesta { IdCdc = a.IdCdc, IdCommessa = a.IdCommessa, IdFase = a.IdFase, Dal = a.Dal, Al = a.Al });
            case "MoveActivity":
                var m = c.Attivita ?? throw new ArgumentException("Date mancanti.");
                await plan.SpostaAsync(c.Id, m.Dal, m.Al); return true;
            case "RemoveActivity": await plan.TogliAsync(c.Id); return true;
            case "Roles": return As<GeCoRoles>(await plan.RuoliAsync());
            case "SetTeamManager": await plan.ImpostaTeamManagerAsync(c.Id, c.Valore); return true;
            case "HoursStart": return As<GeCoHoursStart>(await hours.InizioAsync());
            case "HoursItems": return As<GeCoCostItem[]>(await hours.VociAsync(c.IdCdc, c.Dal));
            case "HoursPeriod": return As<GeCoPeriod>(await hours.PeriodoAsync(c.IdCdc, c.Dal, c.Voci, c.Settimane));
            case "HoursRecent": return As<GeCoRecents>(await hours.RecentiAsync(c.IdCdc, c.Dal, c.Settimane));
            case "HoursProposals": return As<GeCoProposals>(await hours.ProposteAsync(c.IdCdc, c.Dal, c.Settimane));
            case "HoursProjects": return As<GeCoFoundProject[]>(await hours.CercaCommesseAsync(c.Testo, c.Dal, c.Settimane));
            case "HoursRow": return As<GeCoRow>(await hours.NuovaRigaAsync(c.IdCdc, c.Dal, c.IdCommessa, c.IdVdc, c.IdFase, c.Settimane));
            case "HoursSave":
                var s = c.Salvataggio ?? throw new ArgumentException("Ore mancanti.");
                var request = new RichiestaSalvataggio { IdCdc = s.IdCdc, Dal = s.Dal, Settimane = s.Settimane, Celle = As<List<CellaDaSalvare>>(s.Celle ?? []) };
                return As<GeCoSaveResult>(await hours.SalvaAsync(request));
            case "HoursConfirm":
                var k = c.Conferma ?? throw new ArgumentException("Settimana mancante.");
                return As<GeCoConfirmResult>(await hours.ConfermaAsync(new RichiestaConferma { IdCdc = k.IdCdc, Dal = k.Dal, Voci = k.Voci?.ToList(), Conferma = k.Conferma }));
            case "Links" or "Link" or "Unlink" or "ConfirmSafe" or "Folders" or "SyncEvents" or "ViewEvents": return await HandleLinksAsync(c, context, token);
            case "ProjectTeam" or "SetTeamMember" or "SetPermanentTeam": return await HandleTeamAsync(c, context, token);
            default: throw new ArgumentException("Comando GeCo sconosciuto.");
        }
    }

    // I record della libreria e quelli del protocollo hanno gli stessi nomi di proprietà: la copia passa per il JSON,
    // come farebbe il client di GeCo2027, così i dati restano quelli calcolati dalle regole.
    public static T As<T>(object value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, value.GetType())) ?? throw new InvalidDataException("Risposta delle regole vuota.");
}
