using GeCo.Dati;
using GeCo.Dati.Regole;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TA.Server;
using TA.Server.Business;
using TA.Server.SharedDatabase;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Models;
using TA_Gestione_Cartelle_Commesse.Services;

// Allineamento tra GeCo e GeCa (2.40.0) nel servizio, su EF InMemory e cartelle temporanee con dati sintetici: copie ZIP inviate
// come archiviate, cartelle chieste create con la creazione da modello (mai sovrascritture), esiti notificati a chi le ha chieste,
// comandi del modulo Allineamento riservati agli amministratori, clienti di GeCo nel catalogo con l'archivio locale come storico.
// Le regole dell'allineamento sono provate in GeCo2027 (tests/GeCo.Dati.Checks): qui si prova il raccordo con GeCa.
internal static class AlignmentChecks
{
    private const int Soc = 1, UteAdmin = 1, UteCollab = 2, Cdc = 10, CliRossi = 1, CliBianchi = 2, CommA = 100, CommB = 101, CommC = 102, CommD = 103;
    private const int CarGara = 1, CarComm = 2;
    private const string Admin = "admin@studio.example", Collab = "collab@studio.example";

    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "TA-AlignmentChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { await RunAsync(root); }
        finally { if (Path.GetFileName(root).StartsWith("TA-AlignmentChecks-")) Directory.Delete(root, true); }
    }

    private static async Task RunAsync(string root)
    {
        ArchivedCopies(root);

        // ---------------------------------------------------------------- cartelle chieste a GeCa
        var version = AggiornamentoDatabase.VersioneRichiesta;
        var options = new DbContextOptionsBuilder<GeCoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString(), b => b.EnableNullChecks(false))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        using (var db = new TestDb(options)) Seed(db);
        var jobs = Directory.CreateDirectory(Path.Combine(root, "lavori")).FullName;
        var year = Directory.CreateDirectory(Path.Combine(jobs, "2026")).FullName;
        var bids = Directory.CreateDirectory(Path.Combine(root, "gare")).FullName;
        Directory.CreateDirectory(Path.Combine(bids, "2026"));
        var model = Directory.CreateDirectory(Path.Combine(root, "modello")).FullName;
        Directory.CreateDirectory(Path.Combine(model, "01_PROGETTO"));
        var archive = Directory.CreateDirectory(Path.Combine(root, "dati")).FullName;
        var templates = new TemplateCreationService(new ServerSettings { JobsRoot = jobs, BidsRoot = bids, ArchiveFolder = archive, JobTemplate = model });
        var rescanned = new List<string>();
        var runner = new GeCoFolderRequestsService(() => new TestDb(options), _ => Task.FromResult(version), templates.Preview, templates.Create, rescanned.Add);

        async Task<RichiestaCartella> Ask(int project, string? luogo = null)
        {
            using var db = new TestDb(options);
            var admin = db.tblUtenti.AsNoTracking().Single(u => u.Ute_Id == UteAdmin);
            var ctx = new ContestoRegole(db, admin, Soc, db.tblSocieta.AsNoTracking().Single(), version);
            return await new ServizioRichiesteCartelle(ctx, EventiSincronizzazione.GeCo2027).RichiediAsync("commessa", project, null, luogo, null);
        }
        tblRichiesteCartelle Row(int id) { using var db = new TestDb(options); return db.tblRichiesteCartelle.AsNoTracking().Single(r => r.Rca_Id == id); }

        var first = await Ask(CommA, "Ravenna");
        Check(first.Nome == "C26020 - Rossi - Ravenna - Ponte sul fiume", "Richiesta: nome composto dalla libreria con il nome breve del cliente di GeCo");
        // Una cartella diversa con lo stesso codice è già sul disco (la libreria non la conosce ancora): GeCa rifiuta, non sovrascrive.
        var other = Directory.CreateDirectory(Path.Combine(year, "C26021 - Altro cliente - Altro lavoro")).FullName;
        File.WriteAllText(Path.Combine(other, "documento.txt"), "originale");
        var second = await Ask(CommB);
        // Cartella con il nome esatto della richiesta già presente: creata da un tentativo precedente rimasto senza esito.
        var third = await Ask(CommC);
        var leftover = Directory.CreateDirectory(Path.Combine(year, third.Nome)).FullName;

        var outcomes = await runner.RunAsync(default);
        Check(outcomes.Select(o => o.Id).SequenceEqual(new[] { first.Id, second.Id, third.Id }), "Esecuzione: richieste eseguite una alla volta, le più vecchie prima");
        var made = Path.Combine(year, first.Nome);
        Check(Directory.Exists(Path.Combine(made, "01_PROGETTO")) && Row(first.Id) is { Rca_Stato: RichiesteCartelle.Creata, Rca_Esecutore: var executor } r1
            && r1.Rca_Percorso == made && executor == GeCoFolderRequestsService.Executor && rescanned.SequenceEqual(new[] { "C", "C" }),
            "Esecuzione: cartella creata dal modello, esito «Creata» con il percorso, elenco delle commesse riletto");
        Check(Row(second.Id) is { Rca_Stato: RichiesteCartelle.Rifiutata, Rca_Esito: var refused } && refused!.Contains("C26021 - Altro cliente - Altro lavoro")
            && File.ReadAllText(Path.Combine(other, "documento.txt")) == "originale" && Directory.GetDirectories(year, "C26021*").Length == 1,
            "Esecuzione: codice già usato da un'altra cartella, richiesta rifiutata con il motivo e nulla toccato");
        Check(Row(third.Id) is { Rca_Stato: RichiesteCartelle.Creata } r3 && r3.Rca_Percorso == leftover && Directory.GetDirectories(year, "C26022*").Length == 1,
            "Esecuzione: cartella già creata con lo stesso nome riconosciuta, senza crearne un'altra");
        Check((await runner.RunAsync(default)).Count == 0, "Esecuzione: nessuna richiesta eseguita due volte");
        using (var db = new TestDb(options))
        {
            var created = db.tblSincronizzazioneEventi.AsNoTracking().Where(e => e.Sev_Tipo == EventiSincronizzazione.Tipi.CartellaCreata).ToList();
            var ids = created.Select(e => e.Sev_Id).ToList();
            Check(created.Count == 2 && created.All(e => e.Sev_Origine == EventiSincronizzazione.GeCa)
                && db.tblSincronizzazioneDestinatari.AsNoTracking().Where(d => ids.Contains(d.Sde_IdEvento)).All(d => d.Sde_IdUtente == UteAdmin),
                "Esecuzione: esiti registrati con origine GeCa e rivolti a chi ha chiesto la cartella");
        }
        // Modello o disco non disponibili: errore (si può chiedere di nuovo), mai una cartella parziale.
        var broken = new GeCoFolderRequestsService(() => new TestDb(options), _ => Task.FromResult(version),
            new TemplateCreationService(new ServerSettings { JobsRoot = Path.Combine(root, "non-esiste"), ArchiveFolder = archive, JobTemplate = model }).Preview, templates.Create, rescanned.Add);
        var fourth = await Ask(CommD);
        Check((await broken.RunAsync(default)).Single() is { Stato: RichiesteCartelle.Errore } && Row(fourth.Id).Rca_Stato == RichiesteCartelle.Errore && !Directory.EnumerateDirectories(year, "C26023*").Any(),
            "Esecuzione: radice non disponibile, esito «Errore» con il messaggio, nessuna cartella");
        var old = new GeCoFolderRequestsService(() => new TestDb(options), _ => Task.FromResult(13), templates.Preview, templates.Create, rescanned.Add);
        Check((await old.RunAsync(default)).Count == 0, "Esecuzione: database senza la migrazione 14, nulla da fare e nessun errore");

        // Il lettore del registro fa partire subito le richieste all'evento «CartellaRichiesta».
        var hub = new GeCoEventHub();
        var reader = new GeCoEventsService(new ServerSettings(), hub, Microsoft.Extensions.Logging.Abstractions.NullLogger<GeCoEventsService>.Instance, runner);
        using (var db = new TestDb(options)) await reader.PollAsync(db, version, default);
        using (var db = new TestDb(options))
        {
            await EventiSincronizzazione.RegistraAsync(db, version, EventiSincronizzazione.GeCo2027,
                new NuovoEvento(EventiSincronizzazione.Tipi.CartellaRichiesta, "Cartella chiesta.", CommD), UteAdmin, "Amministratore");
            Check(!runner.WakePending && await reader.PollAsync(db, version, default) >= 1 && runner.WakePending, "Lettore degli eventi: «CartellaRichiesta» anticipa l'esecuzione delle richieste");
        }

        // ---------------------------------------------------------------- comandi del modulo Allineamento
        var storeFolder = Directory.CreateDirectory(Path.Combine(root, "gestionale")).FullName;
        var source = new GeCoCustomerSource(() => new TestDb(options), _ => Task.FromResult(version), Soc);
        var business = new BusinessService(new BusinessStore(storeFolder), () => [new WorkProject("p1", "C26020", "Rossi — Ponte")], s => s) { GeCoCustomers = source };
        var service = new GeCoWorkService(() => new TestDb(options), _ => Task.FromResult(version), Soc, (_, _, _) => Task.FromResult(true))
            { FolderRequests = runner, Customers = business, CustomerSource = source };
        async Task<T> Send<T>(GeCoCommand command, string account) =>
            System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(await service.ExecuteAsync(command, account, default)))!;
        async Task<Exception?> Fails(Func<Task> action)
        {
            try { await action(); return null; }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or KeyNotFoundException) { return ex; }
        }

        Check(await Fails(() => Send<GeCoAlignment>(new("Alignment"), Collab)) is UnauthorizedAccessException
            && await Fails(() => Send<GeCoExternalCustomers>(new("ExternalCustomers"), Collab)) is UnauthorizedAccessException
            && await Fails(() => Send<GeCoFolderRequest>(new("RequestFolder", Allineamento: new(Cartella: new("commessa", CommD))), Collab)) is UnauthorizedAccessException,
            "Allineamento: riservato agli amministratori di GeCo, anche per le scritture");
        var report = await Send<GeCoAlignment>(new("Alignment"), Admin);
        Check(report.Commesse.Single(c => c.Id == CommD) is { Situazione: "SenzaCartella", Stato: "Aperta", Richiesta.Stato: RichiesteCartelle.Errore }
            && report.Cartelle.Any(c => c.Cartella.Id == CarGara && c.Creabile == "gara") && report.Richieste.Length == 4,
            "Allineamento: rapporto con le situazioni, le cartelle da cui creare e le richieste, nei record del protocollo");
        var preview = await Send<GeCoFolderPreview>(new("FolderPreview", Allineamento: new(Cartella: new("commessa", CommD, Luogo: "Cesena"))), Admin);
        Check(preview is { Nome: "C26023 - Rossi - Cesena - Rilievo", PuoRichiedere: true, NomeBreveCliente: "Rossi" }, "Allineamento: anteprima del nome con le regole delle cartelle");
        var now = await Send<GeCoFolderRequest>(new("RequestFolder", Valore: true, Allineamento: new(Cartella: new("commessa", CommD, Luogo: "Cesena"))), Admin);
        Check(now is { Stato: RichiesteCartelle.Creata, Origine: EventiSincronizzazione.GeCa } && Directory.Exists(Path.Combine(year, "C26023 - Rossi - Cesena - Rilievo")),
            "Allineamento: «Crea subito» chiede la cartella con origine GeCa e la crea con il modello, con l'esito registrato");
        var notices = await Send<GeCoSyncEvents>(new("SyncEvents"), Admin);
        Check(notices.Eventi.Count(e => e.Tipo == EventiSincronizzazione.Tipi.CartellaCreata && e.Origine == EventiSincronizzazione.GeCa) == 3
            && (await Send<GeCoSyncEvents>(new("SyncEvents"), Collab)).Eventi.All(e => e.Tipo != EventiSincronizzazione.Tipi.CartellaCreata && e.Tipo != EventiSincronizzazione.Tipi.CartellaNonCreata),
            "Notifiche: gli esiti delle cartelle chieste arrivano solo a chi le ha chieste");
        var proposal = await Send<GeCoFromFolderProposal>(new("FromFolderProposal", IdCartella: CarGara), Admin);
        Check(proposal is { Tipo: "gara", Codice: "G26005", Blocchi.Length: 0 } && proposal.Descrizione.Length > 0, "Allineamento: dati proposti per creare la gara dalla cartella");
        var gara = await Send<GeCoCreated>(new("CreateFromFolder", IdCartella: CarGara, Allineamento: new(Nuovo: new("gara", proposal.Codice, proposal.Anno, proposal.Descrizione, CliBianchi))), Admin);
        using (var db = new TestDb(options))
            Check(gara is { Tipo: "gara", Codice: "G26005" } && db.tblCartelleCollegamenti.AsNoTracking().Single(l => l.Ccl_IdGara == gara.Id) is { Ccl_IdCartella: CarGara, Ccl_Origine: EventiSincronizzazione.GeCa },
                "Allineamento: gara creata dalla cartella e collegata, con origine GeCa");
        var commProposal = await Send<GeCoFromFolderProposal>(new("FromFolderProposal", IdCartella: CarComm), Admin);
        var commessa = await Send<GeCoCreated>(new("CreateFromFolder", IdCartella: CarComm, Allineamento: new(Nuovo: new("commessa", commProposal.Codice, commProposal.Anno,
            "Verifica statica", CliRossi, Cdc))), Admin);
        using (var db = new TestDb(options))
            Check(commProposal.Clienti.FirstOrDefault()?.IdCliente == CliRossi && commessa is { Tipo: "commessa", Codice: "C26030" }
                && db.tblCommesse.AsNoTracking().Single(c => c.Cmm_Id == commessa.Id) is { Cmm_IdCliente: CliRossi, Cmm_Descr: "Verifica statica" },
                "Allineamento: commessa creata dalla cartella con i dati confermati e il cliente proposto dal nome breve");

        // ---------------------------------------------------------------- clienti di GeCo nel catalogo di GeCa
        string vatRossi = Vat("0123456789"), vatSolo = Vat("0987654321");
        business.Store.Save("customer", "loc-rossi", 0, new Customer("loc-rossi", "Rossi Srl (vecchia anagrafica)", vatRossi, Revision: 1), "prova", "Customer");
        business.Store.Save("customer", "loc-solo", 0, new Customer("loc-solo", "Studio Nuovo", vatSolo, Address: "Via Uno 1", Revision: 1), "prova", "Customer");
        business.Store.Save("customer", "loc-nome", 0, new Customer("loc-nome", "Bianchi Spa", Revision: 1), "prova", "Customer");
        business.Store.Save("project", "p1", 0, new WorkProject("p1", "C26020", "Rossi — Ponte", CustomerId: "loc-rossi", Revision: 1), "prova", "ProjectDetails");
        var catalog = business.Customers();
        Check(catalog.Count(c => c.FromGeCo) == 2 && catalog.Single(c => c.Id == GeCoCustomerSource.Id(CliRossi)) is { ShortName: "Rossi", Name: "Rossi Srl" }
            && catalog.All(c => c.Id != "loc-rossi") && catalog.Any(c => c.Id == "loc-solo" && !c.FromGeCo) && business.Projects().Single().CustomerId == GeCoCustomerSource.Id(CliRossi),
            "Clienti: catalogo di GeCo con il nome breve; il cliente locale con la stessa P.IVA diventa quello di GeCo anche nelle commesse");
        Check(Throws(() => business.SaveCustomer(new Customer("", "Nuovo cliente locale"), "prova")) is UnauthorizedAccessException, "Clienti: con l'anagrafica di GeCo i clienti locali restano in sola lettura");
        var external = await Send<GeCoExternalCustomers>(new("ExternalCustomers"), Admin);
        Check(external.Clienti.Single(c => c.Id == "loc-rossi") is { Esito: "Abbinato", IdCliente: CliRossi, Commesse: 1 } && external.Clienti.Single(c => c.Id == "loc-solo").Esito == "SoloEsterno"
            && external.Clienti.Single(c => c.Id == "loc-nome") is { Esito: "Proposta", Candidati: [CliBianchi] } && external.GeCo.Length == 2,
            "Clienti: abbinamento dei clienti locali con le regole della libreria (P.IVA, poi nome solo come proposta)");
        Check(await Send<bool>(new("LinkCustomer", Allineamento: new(IdLocale: "loc-nome", IdCliente: CliBianchi)), Admin) && business.Customers().All(c => c.Id != "loc-nome"),
            "Clienti: la proposta confermata da un amministratore collega il cliente locale a quello di GeCo");
        Check(await Fails(() => Send<GeCoCustomerSummary>(new("ImportCustomer", Allineamento: new(Cliente: new("loc-solo", "Studio Nuovo", "Via Uno 1", null, "Forlì", "FC", vatSolo, null))), Admin)) is ArgumentException { Message: "CAP non compilato" },
            "Clienti: importazione in GeCo con i campi obbligatori della pagina Clienti");
        var imported = await Send<GeCoCustomerSummary>(new("ImportCustomer", Allineamento: new(Cliente: new("loc-solo", "Studio Nuovo", "Via Uno 1", "47121", "Forlì", "FC", vatSolo, null, NomeBreve: "Nuovo"))), Admin);
        Check(imported is { RagioneSociale: "Studio Nuovo", NomeBreve: "Nuovo" } && business.Customers().Single(c => c.Id == GeCoCustomerSource.Id(imported.Id)).ShortName == "Nuovo"
            && business.Customers().All(c => c.Id != "loc-solo"), "Clienti: cliente di GeCa importato in GeCo e sostituito nel catalogo");
        Check(await Fails(() => Send<GeCoCustomerSummary>(new("ImportCustomer", Allineamento: new(Cliente: new("loc-solo", "Studio Nuovo", "Via Uno 1", "47121", "Forlì", "FC", vatSolo, null))), Admin)) is ArgumentException a409
            && a409.Message.Contains("abbinarlo invece di importarlo"), "Clienti: un secondo cliente con la stessa P.IVA non viene importato");
        var noArchive = new GeCoWorkService(() => new TestDb(options), _ => Task.FromResult(13), Soc, (_, _, _) => Task.FromResult(true));
        Check((await Fails(() => noArchive.ExecuteAsync(new("Alignment"), Admin, default)))?.Message == GeCoWorkService.AlignmentNotReady, "Allineamento: database senza la migrazione 14, messaggio chiaro");
        Console.WriteLine("AlignmentChecks: copie ZIP, cartelle chieste, notifiche degli esiti, allineamento e clienti di GeCo verificati su dati sintetici.");
    }

    // Copie ZIP/7z accanto alle cartelle: lette solo per nome e metadati e inviate come archiviate; le copie nelle cartelle no.
    private static void ArchivedCopies(string root)
    {
        var jobs = Directory.CreateDirectory(Path.Combine(root, "copie", "lavori")).FullName;
        var year = Directory.CreateDirectory(Path.Combine(jobs, "2026")).FullName;
        File.WriteAllText(Path.Combine(year, "C26012 - Rossi - Ravenna - Ponte.zip"), "non un vero zip: non va aperto");
        File.WriteAllText(Path.Combine(year, "C26014.7z"), "");
        File.WriteAllText(Path.Combine(year, "appunti.zip"), "");
        File.WriteAllText(Path.Combine(year, "G26001 - Ente - Gara.zip"), "");
        var inside = Directory.CreateDirectory(Path.Combine(year, "C26013 - Bianchi - Rilievo")).FullName;
        File.WriteAllText(Path.Combine(inside, "C26013 - Bianchi - allegati.zip"), "");
        File.WriteAllText(Path.Combine(jobs, "C25001 - Fuori anno.zip"), "");
        var copies = ArchivedCopiesReader.Scan(jobs, 'C', default);
        Check(copies.Error == null && !copies.Incomplete && copies.Records.Select(r => r.Code).Order().SequenceEqual(new[] { "C26012", "C26014" })
            && copies.Records.Single(r => r.Code == "C26012") is { Client: "Rossi", Subject: "Ravenna - Ponte", Status: "Archiviata", ArchiveBytes: > 0 },
            "Copie archiviate: ZIP e 7z delle commesse nelle cartelle annuali, per nome; non quelli dentro le cartelle né di altre categorie");
        var bids = Directory.CreateDirectory(Path.Combine(root, "copie", "gare")).FullName;
        File.WriteAllText(Path.Combine(bids, "G26001 - Ente - Gara di prova.zip"), "");
        var container = Directory.CreateDirectory(Path.Combine(bids, "Vecchie gare")).FullName;
        File.WriteAllText(Path.Combine(container, "G25003 - Ente - Gara.7z"), "");
        var bid = Directory.CreateDirectory(Path.Combine(bids, "G26002 - Ente - Gara aperta")).FullName;
        File.WriteAllText(Path.Combine(bid, "G26002 - documenti.zip"), "");
        Check(ArchivedCopiesReader.Scan(bids, 'G', default).Records.Select(r => r.Code).Order().SequenceEqual(new[] { "G25003", "G26001" }),
            "Copie archiviate: gare alla radice e nei contenitori, non dentro le cartelle delle gare");
        var snapshot = new ArchiveSnapshot { Prefix = 'C', Root = jobs, Records = [new FolderRecord("C26013", "26", "013", "Bianchi", "Rilievo", inside, "Valido")] };
        var rows = SharedFolders.Complete(new CategoryState('C', jobs, false, "", "", snapshot), default, out var problem);
        Check(problem == null && rows!.Length == 3 && rows.Single(r => r.Codice == "C26013").Archiviata == false && rows.Where(r => r.Codice != "C26013").All(r => r.Archiviata == true),
            "Database comune: cartelle e copie ZIP della categoria in un solo elenco, le copie segnate come archiviate");
        var archived = new ArchiveSnapshot { Prefix = 'A', Records = [new FolderRecord("G24002", "24", "002", "Ente", "Gara", Path.Combine(root, "archivio", "G24002 - Ente - Gara.zip"), "Valido")] };
        Check(SharedFolders.From(archived).Single() is { Codice: "G24002", Archiviata: true }, "Database comune: le gare in archivio arrivano con il loro codice, archiviate");
        var archiveRoot = Directory.CreateDirectory(Path.Combine(root, "copie", "archivio")).FullName;
        File.WriteAllText(Path.Combine(archiveRoot, "G24002 - Ente - Gara.zip"), "");
        Check(ArchivedJobsReader.Scan(archiveRoot, default, false).Records.Single() is { Code: "G24002", Status: "Valido" }, "Archivio: le gare archiviate hanno il codice G riconosciuto");
    }

    // Partita IVA sintetica con la cifra di controllo giusta (regola di IdentitaClienti).
    private static string Vat(string ten) => Enumerable.Range(0, 10).Select(d => ten + d).First(IdentitaClienti.PartitaIvaValida);
    private static Exception? Throws(Action action) { try { action(); return null; } catch (Exception ex) { return ex; } }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }

    private static void Seed(TestDb db)
    {
        var seen = new DateTime(2026, 11, 1, 8, 0, 0);
        db.tblSocieta.Add(new tblSocieta { Soc_Id = Soc, Soc_Codice = "S", Soc_RagSoc = "Studio di prova" });
        db.tblCentriDiCosto.Add(new tblCentriDiCosto { Cdc_Id = Cdc, Cdc_Codice = "A", Cdc_Descrizione = "Persona A", Cdc_IntExt = "I", Cdc_Attivo = true, Cdc_FlCapocommessa = true });
        db.tblUtenti.AddRange(
            new tblUtenti { Ute_Id = UteAdmin, Ute_Username = "admin", Ute_Descrizione = "Amministratore", Ute_Tipo = "A", Ute_TutteFunzioni = true, Ute_IdCdc = Cdc },
            new tblUtenti { Ute_Id = UteCollab, Ute_Username = "collab", Ute_Descrizione = "Collaboratore", Ute_Tipo = "U", Ute_IdCdc = Cdc });
        db.tblClienti.AddRange(
            new tblClienti { Cli_Id = CliRossi, Cli_Codice = "1", Cli_RagSoc = "Rossi Srl", Cli_NomeBreve = "Rossi", Cli_PartIva = Vat("0123456789") },
            new tblClienti { Cli_Id = CliBianchi, Cli_Codice = "2", Cli_RagSoc = "Bianchi Spa", Cli_NomeBreve = "Bianchi" });
        db.tblClientiSocieta.AddRange(
            new tblClientiSocieta { Scl_IdCliente = CliRossi, Scl_IdSocieta = Soc }, new tblClientiSocieta { Scl_IdCliente = CliBianchi, Scl_IdSocieta = Soc });
        tblCommesse Project(int id, string code, string description) => new()
        { Cmm_Id = id, Cmm_IdSocieta = Soc, Cmm_Anno = 2026, Cmm_Codice = code, Cmm_Postfisso = "", Cmm_Descr = description, Cmm_IdCliente = CliRossi, Cmm_IdResp = Cdc, Cmm_DataIniEff = new DateTime(2026, 1, 1) };
        db.tblCommesse.AddRange(Project(CommA, "C26020", "Ponte sul fiume"), Project(CommB, "C26021", "Scuola"), Project(CommC, "C26022", "Palestra"), Project(CommD, "C26023", "Rilievo"));
        tblCartelle Folder(int id, string path, string category, string code, string client, string subject) => new()
        { Car_Id = id, Car_Percorso = path, Car_Categoria = category, Car_Codice = code, Car_Anno = 2026, Car_Committente = client, Car_Oggetto = subject, Car_Presente = true, Car_PrimaRilevazione = seen, Car_UltimaRilevazione = seen };
        db.tblCartelle.AddRange(
            Folder(CarGara, @"\\server\gare\2026\G26005 - Bianchi - Cesena - Nuova sede", "G", "G26005", "Bianchi", "Cesena - Nuova sede"),
            Folder(CarComm, @"\\server\lavori\2026\C26030 - Rossi - Verifica statica", "C", "C26030", "Rossi", "Verifica statica"));
        db.tblCartelleSincronizzazioni.Add(new tblCartelleSincronizzazioni { Cas_Categoria = "C", Cas_Data = seen, Cas_Origine = "TA-Gestione di prova", Cas_Cartelle = 1 });
        db.SaveChanges();
    }
}
