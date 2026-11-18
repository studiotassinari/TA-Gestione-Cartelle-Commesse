using System.Text.Json;
using GeCo.Dati;
using GeCo.Dati.Regole;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TA.Server.SharedDatabase;
using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;

// Pianificazione e ore lavorate di GeCo nel servizio: chi è l'utente (account Windows del canale → utente di GeCo), che cosa
// può fare e come i risultati delle regole della libreria arrivano al client con i nomi del protocollo. Dati sintetici su EF
// InMemory: le regole stesse sono provate in GeCo2027 (tests/GeCo.Dati.Checks), qui si prova il collegamento.
const int Soc = 1, CdcAdmin = 10, CdcCollab = 11, UteAdmin = 1, UteCollab = 2, UteOther = 3, VoceOre = 100, VoceKm = 101, Comm = 1000, CommOther = 1001, Fase1 = 2000, Fase2 = 2001;
var version = AggiornamentoDatabase.VersioneRichiesta;
var name = Guid.NewGuid().ToString();
var options = new DbContextOptionsBuilder<GeCoDbContext>().UseInMemoryDatabase(name, b => b.EnableNullChecks(false))
    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
using (var db = new TestDb(options)) Seed(db);
var disabled = new HashSet<int> { UteOther };
var service = new GeCoWorkService(() => new TestDb(options), _ => Task.FromResult(version), Soc, (_, user, company) => Task.FromResult(company == Soc && !disabled.Contains(user)));

// Il client riceve la risposta come JSON del protocollo e la legge con i record di TA.Contracts.
async Task<T> Send<T>(GeCoCommand command, string account)
{
    var request = new Request("GeCo", GeCo: command);
    using var memory = new MemoryStream();
    await WireProtocol.WriteAsync(memory, request, default); memory.Position = 0;
    var received = await WireProtocol.ReadAsync<Request>(memory, default);
    Check(received.GeCo == command || received.GeCo?.Action == command.Action, "Comando GeCo trasportato: " + command.Action);
    var reply = Reply.Ok(await service.ExecuteAsync(received.GeCo!, account, default));
    return reply.Data.Deserialize<T>() ?? throw new Exception("Risposta vuota");
}
async Task<Exception?> Fails(Func<Task> action)
{
    try { await action(); return null; }
    catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or KeyNotFoundException) { return ex; }
}
const string Collab = @"DOMINIO\collab", Admin = "admin@studio.example";

// ---------------------------------------------------------------- identità
var access = await Send<GeCoAccess>(new("Access"), Collab);
Check(access is { Recognized: true, User: "collab", Name: "Collaboratore", Hours: true, Planning: true, Company: "Studio di prova" } && access.Match == AccountWindows.ComeNome,
    "Accesso: l'account Windows del canale diventa l'utente di GeCo con lo stesso nome");
var unknown = await Send<GeCoAccess>(new("Access"), @"DOMINIO\sconosciuto");
Check(!unknown.Recognized && unknown.Message.Contains("non corrisponde a nessun utente attivo di GeCo") && !unknown.Hours && !unknown.Planning, "Accesso: account senza utente di GeCo, messaggio chiaro");
Check(await Fails(() => Send<GeCoPlan>(new("Plan"), @"DOMINIO\sconosciuto")) is UnauthorizedAccessException, "Accesso: senza utente di GeCo nessun comando viene eseguito");
var other = await Send<GeCoAccess>(new("Access"), @"DOMINIO\altro");
Check(!other.Recognized && other.Message.Contains("non è abilitato alla società") && await Fails(() => Send<GeCoHoursStart>(new("HoursStart"), @"DOMINIO\altro")) is UnauthorizedAccessException,
    "Accesso: utente non abilitato alla società dello studio, come all'accesso di GeCo2027");
var none = new GeCoWorkService(null, _ => Task.FromResult(0));
Check((await none.ExecuteAsync(new("Access"), Collab, default)) is GeCoAccess { Recognized: false, Message: GeCoWorkService.NotConfigured } && await Fails(() => none.ExecuteAsync(new("Plan"), Collab, default)) is IOException,
    "Database comune non configurato: messaggio e nessun comando");
Check(await Fails(() => service.ExecuteAsync(new("Plan"), "", default)) is UnauthorizedAccessException, "Identità Windows mancante rifiutata");
var old = new GeCoWorkService(() => new TestDb(options), _ => Task.FromResult(6), Soc, (_, _, _) => Task.FromResult(true));
Check((await old.ExecuteAsync(new("Access"), Collab, default)) is GeCoAccess { Recognized: true, Planning: false } a6 && a6.Message.Contains("database non è stato aggiornato")
    && (await Fails(() => old.ExecuteAsync(new("Plan"), Collab, default)))?.Message == "Pianificazione non disponibile: il database non è stato aggiornato.", "Database non aggiornato: pianificazione non disponibile (503 della libreria)");

// ---------------------------------------------------------------- pianificazione
var plan = await Send<GeCoPlan>(new("Plan", Dal: "2026-11-18", Settimane: 1, Persone: [CdcAdmin]), Collab);
Check(plan is { Dal: "2026-11-16", Settimane: 1, OreGiorno: 8, Ruolo: "collaboratore", PuoPianificare: true, PuoOrganizzare: false } && plan.Persone.Single().Id == CdcCollab && plan.Persone[0].Io,
    "Pianificazione: il collaboratore vede solo sé stesso, anche se chiede altri");
Check(plan.Attivita.Length == 2 && plan.Attivita.Single(a => a.IdCommessa == Comm) is { Modificabile: true, Dal: "2026-11-16", Fase: "Progetto" } && !plan.Attivita.Single(a => a.IdCommessa == CommOther).Modificabile
    && plan.Commesse.Single(c => c.Id == Comm) is { Mia: true, Codice: "B260001" } c1 && c1.Fasi.Select(f => f.Descrizione).SequenceEqual(new[] { "Progetto", "Direzione lavori" }),
    "Pianificazione: attività e commesse con i nomi del protocollo, modificabili solo dove si è capocommessa");
var added = await Send<int>(new("AddActivity", Attivita: new(CdcCollab, Comm, Fase2, "2026-11-23", "2026-11-27")), Collab);
Check(added > 0, "Pianificazione: attività aggiunta, con l'identificativo del database comune");
var refused = await Fails(() => Send<int>(new("AddActivity", Attivita: new(CdcCollab, CommOther, null, "2026-11-23", "2026-11-23")), Collab));
Check(refused is UnauthorizedAccessException && refused.Message == "Si può pianificare soltanto sé stessi, sulle commesse di cui si è capocommessa", "Pianificazione: messaggio della libreria per le commesse di altri");
Check((await Fails(() => Send<int>(new("AddActivity", Attivita: new(CdcCollab, Comm, null, "2026-11-27", "2026-11-23")), Collab)))?.Message == "La fine non può precedere l'inizio", "Pianificazione: date controllate dalla libreria");
Check(await Send<bool>(new("MoveActivity", Id: added, Attivita: new(CdcCollab, Comm, Fase2, "2026-11-24", "2026-11-26")), Collab), "Pianificazione: spostamento");
using (var db = new TestDb(options)) Check(db.tblPianificazioneAttivita.Single(a => a.Pat_Id == added) is { Pat_DataInizio.Day: 24, Pat_DataFine.Day: 26, Pat_IdUtenteMod: UteCollab }, "Pianificazione: spostamento scritto con l'utente del token");
Check(await Send<bool>(new("RemoveActivity", Id: added), Collab) && await Fails(() => Send<bool>(new("RemoveActivity", Id: added), Collab)) is KeyNotFoundException, "Pianificazione: attività tolta; una seconda volta non si trova");
Check((await Send<GeCoPlanProject[]>(new("PlanProjects", Testo: "B26"), Collab)).Select(c => c.Id).SequenceEqual(new[] { Comm }), "Pianificazione: ricerca tra le commesse di cui si è capocommessa");
Check(await Fails(() => Send<GeCoRoles>(new("Roles"), Collab)) is UnauthorizedAccessException, "Ruoli e team: riservati agli amministratori");
var roles = await Send<GeCoRoles>(new("Roles"), Admin);
Check(roles.Amministratori.Single().Id == UteAdmin && roles.Utenti.Any(u => u.Id == UteCollab && !u.TeamManager), "Ruoli e team: amministratori e utenti per l'amministratore");
Check(await Send<bool>(new("SetTeamManager", Id: UteCollab, Valore: true), Admin) && (await Send<GeCoPlan>(new("Plan", Persone: [CdcAdmin, CdcCollab]), Collab)).Ruolo == "teammanager",
    "Ruoli e team: il team manager vede la pianificazione di tutti");
await Send<bool>(new("SetTeamManager", Id: UteCollab, Valore: false), Admin);

// ---------------------------------------------------------------- ore lavorate
var start = await Send<GeCoHoursStart>(new("HoursStart"), Collab);
Check(start is { Titolo: "Ore lavorate", PuoCambiareCdc: false, IdCdc: CdcCollab, SettimanaCorrente.Length: 10 } && start.Cdc.Single().Value == CdcCollab && start.VoceKm?.Id == VoceKm && start.Permessi.Ins,
    "Ore: dati iniziali per il collaboratore, voce dei km dalle impostazioni");
var saved = await Send<GeCoSaveResult>(new("HoursSave", Salvataggio: new(null, "2026-11-16", 1, [new(Comm, VoceOre, "2026-11-17", "7:30", Fase1), new(Comm, VoceKm, "2026-11-17", "35", Fase1), new(Comm, VoceOre, "2026-11-30", "2", Fase1)])), Collab);
Check(saved.Salvate == 2 && saved.Errori.Single() is { Messaggio: "Data fuori dal periodo mostrato", IdFase: Fase1, Data: "2026-11-30" }, "Ore: caselle valide salvate, quella fuori periodo con il motivo");
using (var db = new TestDb(options)) Check(db.tblRilevazioni.Single(r => r.Ril_IdVdc == VoceOre).Ril_Input == 7.5m && db.tblRilevazioni.Single(r => r.Ril_IdVdc == VoceKm).Ril_Input == 35, "Ore: 7:30 diventa 7,5 ore; km sulla loro voce");
var period = await Send<GeCoPeriod>(new("HoursPeriod", Dal: "2026-11-16", Settimane: 2, IdCdc: CdcAdmin), Collab);
Check(period is { IdCdc: CdcCollab, Settimane: 2, SolaLettura: false, TipoCentro: "I" } && period.Giorni.Length == 14 && period.VoceKm?.Id == VoceKm
    && period.Righe.Single(r => r.IdVdc == VoceOre && r.IdFase == Fase1) is { Fase: "Progetto", Codice: "B260001" } row && row.Celle[1].Qta == 7.5m && row.Celle.Length == 14,
    "Ore: periodo della propria persona (un'altra non visibile ripiega sulla propria), righe commessa × voce × fase");
var proposals = await Send<GeCoProposals>(new("HoursProposals", Dal: "2026-11-16", Settimane: 1), Collab);
Check(proposals.Piano.Count(p => p.IdCommessa == Comm) == 5 && proposals.Piano.First().Ore > 0 && proposals.Team.Any(t => t.IdCommessa == Comm && t.Fasi.Length == 2), "Ore: proposte dalla pianificazione nei giorni feriali");
var newRow = await Send<GeCoRow>(new("HoursRow", Dal: "2026-11-16", Settimane: 1, IdCommessa: Comm, IdVdc: VoceOre, IdFase: Fase2), Collab);
Check(newRow is { Fase: "Direzione lavori", FaseRichiesta: false } && newRow.Celle.All(c => c.Bloccata == null), "Ore: dati di una riga nuova senza salvare");
Check((await Send<GeCoFoundProject[]>(new("HoursProjects", Dal: "2026-11-16", Settimane: 1, Testo: "B26"), Collab)).Any(c => c.IdCommessa == Comm), "Ore: ricerca rapida tra le commesse aperte");
Check((await Send<GeCoRecents>(new("HoursRecent", Dal: "2026-11-16", Settimane: 1), Collab)).Recenti.Single().IdCommessa == Comm, "Ore: commesse recenti della persona");
var confirmed = await Send<GeCoConfirmResult>(new("HoursConfirm", Conferma: new(null, "2026-11-16", [VoceOre, VoceKm], true)), Collab);
Check(confirmed is { Messaggio: "Rilevazioni confermate con successo", Righe: 2 }, "Ore: conferma della settimana");
var locked = await Send<GeCoSaveResult>(new("HoursSave", Salvataggio: new(null, "2026-11-16", 1, [new(Comm, VoceOre, "2026-11-17", "8", Fase1)])), Collab);
Check(locked.Errori.Single().Messaggio == "Rilevazione confermata: non modificabile", "Ore: rilevazione confermata non modificabile");
Check((await Fails(() => Send<GeCoPeriod>(new("HoursPeriod", Dal: "2026-11-16"), Admin))) is null, "Ore: l'amministratore senza funzione assegnata usa i permessi di tutte le funzioni");
Check(await Fails(() => Send<GeCoSaveResult>(new("HoursSave", Salvataggio: new(CdcAdmin, "2026-11-16", 1, [new(Comm, VoceOre, "2026-11-18", "2")])), Collab)) is UnauthorizedAccessException { Message: "Si possono modificare solo le proprie ore" },
    "Ore: le ore di un'altra persona non si modificano");
Check(await Fails(() => Send<bool>(new("Sconosciuto"), Collab)) is ArgumentException, "Comando GeCo sconosciuto rifiutato");
Check(GeCoWorkService.Translate(new RegolaException("x", 503)) is IOException && GeCoWorkService.Translate(new RegolaException("y", 409)) is ArgumentException { Message: "y" }, "Errori delle regole tradotti con lo stesso messaggio");
Console.WriteLine("GeCoChecks: pianificazione e ore di GeCo verificate su dati sintetici.");

static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }

static void Seed(TestDb db)
{
    var monday = new DateTime(2026, 11, 16);
    db.tblSocieta.Add(new tblSocieta { Soc_Id = 1, Soc_Codice = "S", Soc_RagSoc = "Studio di prova" });
    db.tblCentriDiCosto.AddRange(
        new tblCentriDiCosto { Cdc_Id = 10, Cdc_Codice = "A", Cdc_Descrizione = "Persona A", Cdc_IntExt = "I", Cdc_Attivo = true },
        new tblCentriDiCosto { Cdc_Id = 11, Cdc_Codice = "B", Cdc_Descrizione = "Persona B", Cdc_IntExt = "I", Cdc_Attivo = true });
    db.tblUtenti.AddRange(
        new tblUtenti { Ute_Id = 1, Ute_Username = "admin", Ute_Tipo = "A", Ute_TutteFunzioni = true, Ute_TF_Ins = true, Ute_TF_Mod = true, Ute_TF_Del = true, Ute_IdCdc = 10, Ute_ConfermaStatini = true },
        new tblUtenti { Ute_Id = 2, Ute_Username = "collab", Ute_Descrizione = "Collaboratore", Ute_Tipo = "U", Ute_IdCdc = 11, Ute_ConfermaStatini = true },
        new tblUtenti { Ute_Id = 3, Ute_Username = "altro", Ute_Tipo = "U", Ute_IdCdc = 11 });
    db.tblFunzioni.Add(new tblFunzioni { Fun_Id = 1, Fun_Codice = "statini", Fun_Descrizione = "Ore lavorate", Fun_Menu = "", Fun_Url = "RilevazioneStatini.aspx" });
    db.FunzioniUtenti.Add(new TestFunction { Id = 1, IdUtente = 2, Url = "RilevazioneStatini.aspx", Codice = "statini", Descrizione = "Ore lavorate", Ins = true, Mod = true });
    db.tblVociDiCosto.AddRange(
        new tblVociDiCosto { Vdc_Id = 100, Vdc_Codice = "ORE", Vdc_Descrizione = "Ore", Vdc_ConteggioOre = true },
        new tblVociDiCosto { Vdc_Id = 101, Vdc_Codice = "KM", Vdc_Descrizione = "Chilometri", Vdc_ConteggioOre = false });
    db.tblTariffeCdc.AddRange(new tblTariffeCdc { Tac_Id = 1, Tac_IdSocieta = 1, Tac_IdVdc = 100, Tac_OrdVal = 1 }, new tblTariffeCdc { Tac_Id = 2, Tac_IdSocieta = 1, Tac_IdVdc = 101, Tac_OrdVal = 1 });
    db.tblCoefficienti.AddRange(
        new tblCoefficienti { Coe_Id = 1, Coe_IdTariffa = 1, Coe_CostoBase = 40, Coe_Ricarico = 10, Coe_DataInizioVal = new DateTime(2020, 1, 1) },
        new tblCoefficienti { Coe_Id = 2, Coe_IdTariffa = 2, Coe_CostoBase = 0.5m, Coe_Ricarico = 0, Coe_DataInizioVal = new DateTime(2020, 1, 1) });
    db.tblImpostazioniGeCo2027.Add(new tblImpostazioniGeCo2027 { Imp_Chiave = Impostazioni.VoceChilometri, Imp_Valore = "101", Imp_DataMod = DateTime.Now });
    db.tblClienti.Add(new tblClienti { Cli_Id = 1, Cli_Codice = "C1", Cli_RagSoc = "Cliente di prova" });
    db.tblCommesse.AddRange(
        new tblCommesse { Cmm_Id = 1000, Cmm_IdSocieta = 1, Cmm_Anno = 2026, Cmm_Codice = "B260001", Cmm_Postfisso = "", Cmm_Descr = "Progetto di prova", Cmm_IdCliente = 1, Cmm_IdResp = 11, Cmm_DataIniEff = new DateTime(2026, 1, 1) },
        new tblCommesse { Cmm_Id = 1001, Cmm_IdSocieta = 1, Cmm_Anno = 2026, Cmm_Codice = "N260001", Cmm_Postfisso = "", Cmm_Descr = "Commessa di altri", Cmm_IdCliente = 1, Cmm_IdResp = 10, Cmm_DataIniEff = new DateTime(2026, 1, 1) });
    db.tblFasiCommesse.AddRange(
        new tblFasiCommesse { Fco_Id = 2000, Fco_IdCommessa = 1000, Fco_Descrizione = "Progetto", Fco_Ordinamento = 1 },
        new tblFasiCommesse { Fco_Id = 2001, Fco_IdCommessa = 1000, Fco_Descrizione = "Direzione lavori", Fco_Ordinamento = 2 });
    db.tblPianificazioneAttivita.AddRange(
        new tblPianificazioneAttivita { Pat_Id = 1, Pat_IdCdc = 11, Pat_IdCommessa = 1000, Pat_IdFase = 2000, Pat_DataInizio = monday, Pat_DataFine = monday.AddDays(4), Pat_DataMod = DateTime.Now, Pat_IdUtenteMod = 1 },
        new tblPianificazioneAttivita { Pat_Id = 2, Pat_IdCdc = 11, Pat_IdCommessa = 1001, Pat_DataInizio = monday.AddDays(2), Pat_DataFine = monday.AddDays(3), Pat_DataMod = DateTime.Now, Pat_IdUtenteMod = 1 });
    db.SaveChanges();
}

// Funzione assegnata a un utente: in SQL Server vwFunzioni_x_Utente è una vista, che EF InMemory ricava da questa tabella.
public class TestFunction
{
    public int Id { get; set; }
    public int IdUtente { get; set; }
    public string Url { get; set; } = "";
    public string Codice { get; set; } = "";
    public string Descrizione { get; set; } = "";
    public bool Ins { get; set; }
    public bool Mod { get; set; }
    public bool Del { get; set; }
}
public class TestDb(DbContextOptions<GeCoDbContext> options) : GeCoDbContext(options)
{
    public DbSet<TestFunction> FunzioniUtenti => Set<TestFunction>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<TestFunction>().HasKey(f => f.Id);
        modelBuilder.Entity<vwFunzioni_x_Utente>().ToInMemoryQuery(() => FunzioniUtenti.Select(f => new vwFunzioni_x_Utente
        { Ufu_IdUtente = f.IdUtente, Fun_Url = f.Url, Fun_Codice = f.Codice, Fun_Descrizione = f.Descrizione, Fun_Menu = "", Ufu_Ins = f.Ins, Ufu_Mod = f.Mod, Ufu_Del = f.Del }));
    }
}
