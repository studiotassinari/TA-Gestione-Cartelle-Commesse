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
const int CommDeleted = 9999, CarComm = 1, CarOther = 2, CarGara = 3, CarOrphan = 4;
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
using (var db = new TestDb(options))
    Check(db.tblAccountWindowsVisti.AsNoTracking().Single() is { Awv_Account: Collab, Awv_Applicazione: AccountWindowsVisti.GeCa, Awv_IdUtente: UteCollab, Awv_Volte: 1 },
        "Accesso: l'account Windows è registrato tra quelli visti da GeCa, con l'utente trovato");
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
Check(refused is UnauthorizedAccessException && refused.Message == "Si può pianificare soltanto sé stessi, sulle commesse di cui si è capocommessa o del proprio team", "Pianificazione: messaggio della libreria per le commesse di altri");
Check((await Fails(() => Send<int>(new("AddActivity", Attivita: new(CdcCollab, Comm, null, "2026-11-27", "2026-11-23")), Collab)))?.Message == "La fine non può precedere l'inizio", "Pianificazione: date controllate dalla libreria");
Check(await Send<bool>(new("MoveActivity", Id: added, Attivita: new(CdcCollab, Comm, Fase2, "2026-11-24", "2026-11-26")), Collab), "Pianificazione: spostamento");
using (var db = new TestDb(options)) Check(db.tblPianificazioneAttivita.Single(a => a.Pat_Id == added) is { Pat_DataInizio.Day: 24, Pat_DataFine.Day: 26, Pat_IdUtenteMod: UteCollab }, "Pianificazione: spostamento scritto con l'utente del token");
Check(await Send<bool>(new("RemoveActivity", Id: added), Collab) && await Fails(() => Send<bool>(new("RemoveActivity", Id: added), Collab)) is KeyNotFoundException, "Pianificazione: attività tolta; una seconda volta non si trova");
Check((await Send<GeCoPlanProject[]>(new("PlanProjects", Testo: "B26"), Collab)).Select(c => c.Id).SequenceEqual(new[] { Comm }), "Pianificazione: ricerca tra le commesse di cui si è capocommessa o del proprio team");
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
// ---------------------------------------------------------------- collegamento cartelle (2.38.0)
// Le proposte e le scritture sono di ServizioCollegamentiCartelle (provato in GeCo2027): qui chi può, l'origine «GeCa», i nomi
// del protocollo, la sostituzione esplicita, l'anteprima delle proposte sicure e gli errori della libreria con il loro messaggio.
var adminAccess = await Send<GeCoAccess>(new("Access"), Admin);
Check(adminAccess is { Recognized: true, Administrator: true, Links: true } && access is { Administrator: false, Links: true }, "Accesso: amministratore di GeCo e collegamento cartelle disponibile, decisi dal servizio");
Check(await Fails(() => Send<GeCoLinks>(new("Links"), Collab)) is UnauthorizedAccessException { Message: "Funzione riservata agli amministratori" }
    && await Fails(() => Send<int>(new("Link", Tipo: "commessa", Id: Comm, IdCartella: CarComm), Collab)) is UnauthorizedAccessException, "Collegamento cartelle: riservato agli amministratori, anche per le scritture");
var links = await Send<GeCoLinks>(new("Links"), Admin);
var rowComm = links.Elementi.Single(r => r.Tipo == "commessa" && r.Id == Comm);
Check(rowComm is { Codice: "B260001", Livello: "sicura", Collegamento: null, Proposta: { IdCartella: CarComm, Punteggio: 100, Cartella.Nome: "B260001 - Cliente di prova - Progetto di prova" } }
    && links.Elementi.Single(r => r.Tipo == "gara").Proposta?.IdCartella == CarGara && links.Sicure.Length == 3 && links.Sincronizzazioni.Single().Categoria == "C"
    && links.CartelleLibere.Any(f => f.Cartella.Id == CarOrphan && f.CandidataPer.Length == 0) && links.Conflitti.Any(c => c.Tipo == "SenzaCommessa" && c.IdCartella == CarOrphan),
    "Collegamento cartelle: proposte sicure per codice, cartella senza commessa segnalata, nomi del protocollo");
var stale = links.Sicure;
Check(await Send<int>(new("Link", Tipo: "commessa", Id: Comm, IdCartella: CarComm), Admin) > 0, "Collegamento cartelle: proposta confermata");
using (var db = new TestDb(options))
    Check(db.tblCartelleCollegamenti.Single() is { Ccl_IdCartella: CarComm, Ccl_IdCommessa: Comm, Ccl_Origine: EventiSincronizzazione.GeCa, Ccl_IdUtente: UteAdmin }
        && db.tblSincronizzazioneEventi.Single(e => e.Sev_Tipo == EventiSincronizzazione.Tipi.Collegata) is { Sev_Origine: EventiSincronizzazione.GeCa, Sev_IdCommessa: Comm, Sev_IdUtente: UteAdmin },
        "Collegamento cartelle: scritto con l'utente del token e l'origine GeCa, con l'evento per GeCo2027");
var replace = await Fails(() => Send<int>(new("Link", Tipo: "commessa", Id: Comm, IdCartella: CarOther), Admin));
Check(replace is ArgumentException && replace.Message.Contains("confermare la sostituzione"), "Collegamento cartelle: un elemento già collegato si ricollega solo con la sostituzione esplicita");
Check((await Fails(() => Send<int>(new("ConfirmSafe", Collegamenti: stale), Admin)))?.Message.Contains("Le proposte sono cambiate") == true, "Collegamento cartelle: anteprima superata, nessuna proposta confermata");
var fresh = (await Send<GeCoLinks>(new("Links"), Admin)).Sicure;
Check(fresh.Length == 2 && await Send<int>(new("ConfirmSafe", Collegamenti: fresh), Admin) == 2, "Collegamento cartelle: proposte sicure confermate in blocco dopo l'anteprima");
var choices = await Send<GeCoFolderChoice[]>(new("Folders", Testo: "B26", Tipo: "commessa"), Admin);
Check(choices.Single().Cartella.Id == CarComm && choices[0].CollegataA is { Tipo: "commessa", Id: Comm } && (await Send<GeCoFolderChoice[]>(new("Folders", Tipo: "gara"), Admin)).All(c => c.Cartella.Categoria is "G" or "A"),
    "Collegamento cartelle: ricerca delle cartelle della categoria ammessa, con quelle già collegate indicate");
Check(await Send<bool>(new("Unlink", Tipo: "commessa", Id: Comm), Admin) && !await Send<bool>(new("Unlink", Tipo: "commessa", Id: Comm), Admin), "Collegamento cartelle: scollegata; una seconda volta non c'è nulla da togliere");
Check(await Fails(() => Send<bool>(new("Unlink", Tipo: "cartella", Id: Comm), Admin)) is ArgumentException, "Collegamento cartelle: tipo non previsto rifiutato dalla libreria");
var old11 = new GeCoWorkService(() => new TestDb(options), _ => Task.FromResult(11), Soc, (_, _, _) => Task.FromResult(true));
Check((await old11.ExecuteAsync(new("Access"), Admin, default)) is GeCoAccess { Links: false } && (await Fails(() => old11.ExecuteAsync(new("Links"), Admin, default)))?.Message == GeCoWorkService.LinksNotReady
    && (await old11.ExecuteAsync(new("SyncEvents"), Collab, default)) is GeCoSyncEvents { Disponibile: false }, "Database senza la migrazione 12: collegamento e notifiche non disponibili, con il motivo");

// ---------------------------------------------------------------- notifiche da GeCo2027
long Register(string type, string text, int? project)
{
    using var db = new TestDb(options);
    return EventiSincronizzazione.RegistraAsync(db, version, EventiSincronizzazione.GeCo2027, new NuovoEvento(type, text, project), UteAdmin, "Amministratore").GetAwaiter().GetResult()!.Value;
}
var hub = new GeCoEventHub();
var reader = new GeCoEventsService(new TA.Server.ServerSettings(), hub, Microsoft.Extensions.Logging.Abstractions.NullLogger<GeCoEventsService>.Instance);
using (var db = new TestDb(options)) Check(await reader.PollAsync(db, 11, default) == -1 && reader.Position == null, "Lettore degli eventi: in attesa senza la migrazione 12");
using (var db = new TestDb(options))
{
    Check(await reader.PollAsync(db, version, default) == 0 && reader.Position == await EventiSincronizzazione.UltimoIdAsync(db) && hub.Last == reader.Position,
        "Lettore degli eventi: al primo avvio parte dall'ultimo evento, senza notificare la cronologia");
    Check(await EventiSincronizzazione.PosizioneAsync(db, GeCoEventsService.Consumer) == reader.Position
        && db.tblSincronizzazionePosizioni.AsNoTracking().Single().Sip_OrigineLetta == EventiSincronizzazione.GeCo2027, "Lettore degli eventi: posizione «GeCa» salvata subito, sugli eventi di GeCo2027");
}
var created = Register(EventiSincronizzazione.Tipi.CommessaCreata, "Commessa B260001 creata da Amministratore.", Comm);
var foreign = Register(EventiSincronizzazione.Tipi.CommessaEliminata, "Commessa Z260001 eliminata.", CommDeleted);
using var watching = hub.Subscribe(); watching.TryRead(out _);
using (var db = new TestDb(options))
{
    var folder = await EventiSincronizzazione.RegistraAsync(db, version, EventiSincronizzazione.GeCa, new NuovoEvento(EventiSincronizzazione.Tipi.CartellaNuova, "Nuova cartella."), null, "TA-Gestione");
    Check(await reader.PollAsync(db, version, default) == 2 && reader.Position == folder && watching.TryRead(out var pushed) && pushed == foreign && hub.Summary == "2 novità da GeCo2027"
        && await EventiSincronizzazione.PosizioneAsync(db, GeCoEventsService.Consumer) == folder, "Lettore degli eventi: notifiche solo da quelli di GeCo2027, client avvisati, posizione salvata dopo l'ultimo letto");
    Check(await reader.PollAsync(db, version, default) == 0 && !watching.TryRead(out _), "Lettore degli eventi: nessuna notifica ripetuta");
}
var restarted = new GeCoEventsService(new TA.Server.ServerSettings(), new GeCoEventHub(), Microsoft.Extensions.Logging.Abstractions.NullLogger<GeCoEventsService>.Instance);
using (var db = new TestDb(options)) Check(await restarted.PollAsync(db, version, default) == 0 && restarted.Position == reader.Position, "Lettore degli eventi: dopo un riavvio riprende dalla posizione salvata");
var notices = await Send<GeCoSyncEvents>(new("SyncEvents"), Collab);
Check(notices is { Disponibile: true, UltimoId: var lastGeCo } && lastGeCo == foreign && notices.Eventi.Any(e => e.Id == created && e.Tipo == "CommessaCreata" && e.Utente == "Amministratore" && e.Data.Length == 16)
    && notices.Eventi.All(e => e.Origine == EventiSincronizzazione.GeCo2027 && e.Id != foreign), "Notifiche: eventi di GeCo2027, senza quelli di commesse che l'utente non può vedere (eliminate)");
Check((await Send<GeCoSyncEvents>(new("SyncEvents", DopoId: foreign), Collab)).Eventi.Length == 0 && (await Send<GeCoSyncEvents>(new("SyncEvents", DopoId: created - 1), Admin)).Eventi.Select(e => e.Id).SequenceEqual(new[] { foreign, created }),
    "Notifiche: solo quelle dopo l'ultima ricevuta; l'amministratore vede anche le commesse eliminate");

// ---------------------------------------------------------------- pianificazione e ore aggiornate subito (eventi tecnici, 2.39.0)
async Task<long> Last() { using var db = new TestDb(options); return await EventiSincronizzazione.UltimoIdAsync(db); }
EventoSincronizzazione LastEvent() { using var db = new TestDb(options); var r = db.tblSincronizzazioneEventi.AsNoTracking().OrderByDescending(e => e.Sev_Id).First();
    return new(r.Sev_Id, r.Sev_Data, r.Sev_Origine, r.Sev_Tipo, r.Sev_IdCommessa, r.Sev_IdGara, r.Sev_IdCartella, r.Sev_Codice, r.Sev_Percorso, r.Sev_Testo, r.Sev_IdUtente, r.Sev_Utente); }
var beforeSaves = await Last();
var startView = await Send<GeCoViewUpdates>(new("ViewEvents"), Collab);
Check(startView is { Disponibile: true, Eventi.Length: 0 }, "Viste: senza DopoId solo il punto di partenza");
Check(await Send<int>(new("AddActivity", Attivita: new(CdcCollab, Comm, Fase1, "2026-11-23", "2026-11-25"), Scheda: "geca-prova1"), Collab) > 0, "Viste: attività aggiunta dalla finestra che salva");
var movedEvent = LastEvent();
Check(movedEvent is { Origine: EventiSincronizzazione.GeCa, Tipo: EventiSincronizzazione.Tipi.PianificazioneModificata, Codice: EventiSincronizzazione.MarcaTecnica, IdUtente: UteCollab }
    && EventiSincronizzazione.Aggiornamento(movedEvent) is { IdCdc: CdcCollab, Scheda: "geca-prova1" } m && m.Dal == new DateTime(2026, 11, 23) && m.Al == new DateTime(2026, 11, 25) && m.Commesse.Contains(Comm),
    "Viste: l'attività aggiunta in GeCa registra un evento tecnico di origine GeCa con persona, giorni, commessa e finestra");
var hoursSaved = await Send<GeCoSaveResult>(new("HoursSave", Salvataggio: new(null, "2026-11-16", 1, [new(Comm, VoceOre, "2026-11-18", "3", Fase1)]), Scheda: "non valida!"), Collab);
var hoursEvent = LastEvent();
Check(hoursSaved.Salvate == 1 && hoursEvent is { Origine: EventiSincronizzazione.GeCa, Tipo: EventiSincronizzazione.Tipi.OreModificate } && EventiSincronizzazione.Aggiornamento(hoursEvent) is { IdCdc: CdcCollab, Scheda: null },
    "Viste: le ore salvate in GeCa registrano un evento tecnico; una scheda non valida viene ignorata");
Check(GeCoWorkService.Saves("HoursSave") && GeCoWorkService.Saves("MoveActivity") && !GeCoWorkService.Saves("Plan") && !GeCoWorkService.Saves("ViewEvents"), "Viste: solo i salvataggi anticipano la lettura del registro");
long otherPerson;
using (var db = new TestDb(options))
{
    // Ore di un'altra persona salvate in GeCo2027 da una scheda del browser.
    var adminUser = db.tblUtenti.AsNoTracking().Single(u => u.Ute_Id == UteAdmin);
    EventiSincronizzazione.AggiungiAggiornamento(new ContestoRegole(db, adminUser, Soc, null, version) { Origine = EventiSincronizzazione.GeCo2027, Scheda = "scheda-browser" },
        EventiSincronizzazione.Tipi.OreModificate, CdcAdmin, new DateTime(2026, 11, 17), new DateTime(2026, 11, 17), [Comm], "Ore di Persona A modificate");
    await db.SaveChangesAsync();
    otherPerson = await EventiSincronizzazione.UltimoIdAsync(db);
}
var collabViews = await Send<GeCoViewUpdates>(new("ViewEvents", DopoId: beforeSaves), Collab);
Check(collabViews is { Disponibile: true, Parziale: false } && collabViews.UltimoId == otherPerson && collabViews.Eventi.Select(e => e.Id).SequenceEqual(new[] { movedEvent.Id, hoursEvent.Id })
    && collabViews.Eventi[0] is { Origine: "GeCa", Tipo: "PianificazioneModificata", IdCdc: CdcCollab, Dal: "2026-11-23", Al: "2026-11-25", Scheda: "geca-prova1", Utente: "Collaboratore" },
    "Viste: il collaboratore riceve i dati dei salvataggi della propria persona, non le ore di un altro");
var adminViews = await Send<GeCoViewUpdates>(new("ViewEvents", DopoId: beforeSaves), Admin);
Check(adminViews.Eventi.Length == 3 && adminViews.Eventi[^1] is { Origine: "GeCo2027", IdCdc: CdcAdmin, Scheda: "scheda-browser" } && adminViews.Eventi[^1].Commesse.SequenceEqual(new[] { Comm }),
    "Viste: l'amministratore riceve anche i salvataggi delle altre persone, con origine e scheda");
Check((await Send<GeCoViewUpdates>(new("ViewEvents", DopoId: otherPerson), Admin)).Eventi.Length == 0, "Viste: nulla dopo l'ultimo evento ricevuto");
var syncAfter = await Send<GeCoSyncEvents>(new("SyncEvents", DopoId: beforeSaves), Admin);
Check(syncAfter.Eventi.Length == 0 && syncAfter.UltimoId == foreign, "Notifiche: gli eventi tecnici di pianificazione e ore non sono notifiche e non spostano l'ultima");
using var views = hub.Subscribe(views: true); views.TryReadViews(out _); views.TryRead(out _);
using var oldClient = hub.Subscribe();
using (var db = new TestDb(options))
{
    var summary = hub.Summary;
    Check(await reader.PollAsync(db, version, default) == 0 && views.TryReadViews(out var viewId) && viewId == otherPerson && !views.TryRead(out _) && !oldClient.TryReadViews(out _)
        && hub.Summary == summary && reader.Position == otherPerson,
        "Lettore degli eventi: gli eventi tecnici di GeCa e di GeCo2027 aggiornano le viste dei client che le chiedono, non sono novità");
}
// Richieste di accesso (migrazione 13): notifiche rivolte a utenti precisi, solo per loro anche se altri vedono la commessa.
long aimed;
using (var db = new TestDb(options))
    aimed = (await NotificheMirate.RegistraAsync(db, version, EventiSincronizzazione.GeCo2027, new NuovoEvento(EventiSincronizzazione.Tipi.AccessoRichiesto, "Richiesta di accesso a B260001.", Comm),
        UteCollab, "Collaboratore", [UteAdmin]))!.Value;
Check((await Send<GeCoSyncEvents>(new("SyncEvents", DopoId: beforeSaves), Admin)).Eventi.Select(e => e.Id).SequenceEqual(new[] { aimed })
    && (await Send<GeCoSyncEvents>(new("SyncEvents", DopoId: beforeSaves), Collab)).Eventi.Length == 0,
    "Notifiche mirate: la richiesta di accesso la vede solo il destinatario, non chi vede la commessa");
using (var db = new TestDb(options))
    Check(await reader.PollAsync(db, version, default) == 1 && watching.TryRead(out var aimedPush) && aimedPush == aimed && hub.Summary == null,
        "Lettore degli eventi: una notifica mirata avvisa i client senza riepilogo per tutti (i destinatari la vedono nell'elenco)");
// ---------------------------------------------------------------- team della commessa (2.41.0)
// Le regole sono di ServizioTeamCommessa (provato in GeCo2027): qui il comando, i permessi dal token, l'origine «GeCa», l'evento
// tecnico per le viste aperte e la notifica mirata a chi entra nel team.
var beforeTeam = await Last();
var byCode = await Send<GeCoProjectTeamResult>(new("ProjectTeam", Testo: "B260001"), Collab);
Check(byCode is { Trovata: true, Team: { IdCommessa: Comm, Codice: "B260001", PuoModificare: true } } && byCode.Team.Componenti.Single() is { Id: CdcCollab, Capocommessa: true, Ruolo: "Capocommessa", Iniziali: "PB" }
    && byCode.Team.Candidati!.Select(p => p.Id).SequenceEqual(new[] { CdcAdmin, CdcCollab }),
    "Team: la commessa dal codice della cartella, il capocommessa può modificarla, persone candidabili");
var notFound = await Send<GeCoProjectTeamResult>(new("ProjectTeam", Testo: "C26999"), Collab);
Check(notFound is { Trovata: false, Team: null } && notFound.Message.Contains("C26999"), "Team: codice senza commessa di GeCo, con il motivo");
var otherTeam = await Send<GeCoProjectTeamResult>(new("ProjectTeam", IdCommessa: CommOther), Collab);
Check(otherTeam.Team is { PuoModificare: false, Candidati: null } && await Fails(() => Send<GeCoProjectTeam>(new("SetTeamMember", IdCommessa: CommOther, IdCdc: CdcCollab, Valore: true), Collab))
    is UnauthorizedAccessException { Message: ServizioTeamCommessa.NonAutorizzato }, "Team: chi non è capocommessa, team manager o amministratore lo vede soltanto");
var withAdmin = await Send<GeCoProjectTeam>(new("SetTeamMember", IdCommessa: Comm, IdCdc: CdcAdmin, Valore: true, Scheda: "geca-team1"), Collab);
var teamEvent = LastEvent();
Check(withAdmin.Membri.Single().Id == CdcAdmin && withAdmin.Componenti.Length == 2 && GeCoWorkService.Saves("SetTeamMember") && GeCoWorkService.Saves("SetPermanentTeam") && !GeCoWorkService.Saves("ProjectTeam"),
    "Team: persona aggiunta dal client di GeCa; il salvataggio anticipa la lettura del registro");
using (var db = new TestDb(options))
{
    var change = db.tblSincronizzazioneEventi.AsNoTracking().Single(e => e.Sev_Id > beforeTeam && e.Sev_Tipo == EventiSincronizzazione.Tipi.TeamModificato);
    Check(change is { Sev_Origine: EventiSincronizzazione.GeCa, Sev_IdCommessa: Comm, Sev_Codice: EventiSincronizzazione.MarcaTecnica, Sev_IdUtente: UteCollab }
        && teamEvent is { Tipo: EventiSincronizzazione.Tipi.AggiuntoAlTeam, Origine: EventiSincronizzazione.GeCa } && teamEvent.Testo.StartsWith("Sei stato aggiunto al team della commessa B260001")
        && db.tblSincronizzazioneDestinatari.AsNoTracking().Where(d => d.Sde_IdEvento == teamEvent.Id).Select(d => d.Sde_IdUtente).ToList().SequenceEqual(new[] { UteAdmin }),
        "Team: evento tecnico di origine GeCa e notifica mirata a chi entra nel team");
}
var teamViews = await Send<GeCoViewUpdates>(new("ViewEvents", DopoId: beforeTeam), Collab);
Check(teamViews.Eventi.Single() is { Tipo: "TeamModificato", IdCdc: 0, Dal: "", Scheda: "geca-team1", Utente: "Collaboratore" } u && u.Commesse.SequenceEqual(new[] { Comm }),
    "Team: l'evento arriva alle viste aperte di chi vede la commessa, con la commessa e la finestra che ha salvato");
Check((await Send<GeCoSyncEvents>(new("SyncEvents", DopoId: beforeTeam), Admin)).Eventi.Select(e => e.Tipo).SequenceEqual(new[] { "AggiuntoAlTeam" })
    && (await Send<GeCoSyncEvents>(new("SyncEvents", DopoId: beforeTeam), Collab)).Eventi.Length == 0,
    "Team: la notifica «aggiunto al team» scritta da GeCa arriva al destinatario, l'evento tecnico non è una notifica");
using (var db = new TestDb(options))
    Check(await reader.PollAsync(db, version, default) == 1 && views.TryReadViews(out var teamView) && teamView == teamEvent.Id - 1 && watching.TryRead(out var teamPush) && teamPush == teamEvent.Id,
        "Lettore degli eventi: il team modificato aggiorna le viste, la notifica mirata avvisa i client");
Check(await Fails(() => Send<GeCoProjectTeam>(new("SetPermanentTeam", IdCommessa: Comm, Id: 77, Valore: true), Admin)) is KeyNotFoundException
    && await Fails(() => new GeCoWorkService(() => new TestDb(options), _ => Task.FromResult(8), Soc, (_, _, _) => Task.FromResult(true)).ExecuteAsync(new("SetPermanentTeam", IdCommessa: Comm, Id: 1, Valore: true), Admin, default)) is IOException,
    "Team permanente: team inesistente = non trovato; database prima della migrazione 9 = non disponibile");
var removed = await Send<GeCoProjectTeam>(new("SetTeamMember", IdCommessa: Comm, IdCdc: CdcAdmin, Valore: false), Admin);
Check(removed.Membri.Length == 0 && GeCoWorkService.View(LastEvent()) is { Tipo: "TeamModificato", Commesse: [Comm] }, "Team: persona tolta dall'amministratore, evento per le viste");
await AlignmentChecks.RunAsync();
Console.WriteLine("GeCoChecks: pianificazione, ore, collegamento cartelle, notifiche, team della commessa e allineamento di GeCo verificati su dati sintetici.");

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
    // Collegamento cartelle: cartelle e una gara sintetiche, una cartella senza commessa.
    db.tblGare.Add(new tblGare { Gar_Id = 1, Gar_Codice = "G26001", Gar_Anno = 2026, Gar_Oggetto = "Gara di prova", Gar_Ente = "Ente di prova" });
    var seen = new DateTime(2026, 11, 1, 8, 0, 0);
    tblCartelle Folder(int id, string path, string category, string code, string client, string subject) => new()
    { Car_Id = id, Car_Percorso = path, Car_Categoria = category, Car_Codice = code, Car_Anno = 2026, Car_Committente = client, Car_Oggetto = subject, Car_Presente = true, Car_PrimaRilevazione = seen, Car_UltimaRilevazione = seen };
    db.tblCartelle.AddRange(
        Folder(1, @"\\server\lavori\2026\B260001 - Cliente di prova - Progetto di prova", "C", "B260001", "Cliente di prova", "Progetto di prova"),
        Folder(2, @"\\server\lavori\2026\N260001 - Cliente di prova - Commessa di altri", "C", "N260001", "Cliente di prova", "Commessa di altri"),
        Folder(3, @"\\server\gare\2026\G26001 - Ente di prova - Gara di prova", "G", "G26001", "Ente di prova", "Gara di prova"),
        Folder(4, @"\\server\lavori\2026\C26999 - Senza commessa", "C", "C26999", "Senza", "commessa"));
    db.tblCartelleSincronizzazioni.Add(new tblCartelleSincronizzazioni { Cas_Categoria = "C", Cas_Data = seen, Cas_Origine = "TA-Gestione di prova", Cas_Cartelle = 3 });
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
