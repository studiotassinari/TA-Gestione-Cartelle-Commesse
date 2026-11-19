using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// Allineamento tra GeCo e GeCa (2.40.0, pagina Allineamento di GeCo2027): commesse con le loro cartelle (comprese quelle in
// archivio), cartelle senza commessa né gara, clienti di GeCo da sistemare (nome breve, P.IVA, doppioni), committenti delle
// cartelle, clienti dell'archivio locale di GeCa da abbinare o importare in GeCo, cartelle chieste. Il servizio decide chi è
// amministratore, calcola e scrive con le regole della libreria GeCo.Dati: qui ci sono soltanto sezione, filtri, ultimo esito
// e le domande di conferma. Proposte e candidati sono sempre indicati come tali, con il motivo.
public sealed class AlignmentViewModel(IGeCoGateway gateway) : IDisposable
{
    public const int RequiredVersion = 14;
    public const string Projects = "Commesse", Folders = "Cartelle senza commessa", Customers = "Clienti di GeCo da sistemare", Clients = "Committenti delle cartelle",
        External = "Clienti di GeCa", Requests = "Cartelle chieste";
    public static readonly string[] Sections = [Projects, Folders, Customers, Clients, External, Requests];
    public const string ToFix = "Da sistemare", All = "Tutte";
    public static readonly string[] ProjectViews = [ToFix, All];
    private static readonly HashSet<string> Fix = ["InArchivio", "DaCollegare", "DaRicollegare", "CartellaSparita", "SenzaCartella"];
    private readonly CancellationTokenSource lifetime = new();
    public GeCoAccess? Access { get; private set; }
    public GeCoAlignment? Report { get; private set; }
    public GeCoExternalCustomers? Local { get; private set; }
    public string Error { get; private set; } = "";
    public string Done { get; private set; } = "";
    public string LocalMessage { get; private set; } = "";
    public bool Busy { get; private set; }
    public string Section { get; set; } = Projects;
    public string View { get; set; } = ToFix;
    public string Filter { get; set; } = "";
    // «Comprese archiviate»: commesse chiuse o archiviate e cartelle in archivio (ZIP), come nella pagina di GeCo2027.
    public bool IncludeArchived { get; set; }
    public event Action? Changed;
    public Func<string, bool> Ask { get; set; } = _ => true;
    public void Dispose() { lifetime.Cancel(); lifetime.Dispose(); }

    public bool Allowed => Access is { Recognized: true, Administrator: true, DatabaseVersion: >= RequiredVersion };
    public string Denied => Access switch
    {
        null => "",
        { Recognized: false } a => a.Message,
        { DatabaseVersion: < RequiredVersion } => "Allineamento non disponibile: il database comune non è ancora aggiornato (migrazione 14). Aggiornare prima GeCo2027 o il servizio.",
        { Administrator: false } => "L'allineamento tra GeCo e GeCa è riservato agli amministratori di GeCo (tipo «Amministratore» o «Tutte le funzioni» nella pagina Utenti).",
        _ => "",
    };
    public static string Message(Exception ex) => ex.Message is "Comando non supportato." or "Comando GeCo sconosciuto."
        ? "Il server non gestisce ancora l'allineamento: aggiornare il servizio alla versione 2.40.0 o successiva." : ex.Message;

    public async Task StartAsync()
    {
        try { Access = await gateway.Send<GeCoAccess>(new("Access"), lifetime.Token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Access = new(false, Message(ex), ""); }
        if (Allowed) await LoadAsync(); else Changed?.Invoke();
    }

    public async Task LoadAsync()
    {
        try
        {
            Busy = true; Changed?.Invoke();
            Report = await gateway.Send<GeCoAlignment>(new("Alignment"), lifetime.Token);
            Error = "";
            // I clienti dell'archivio locale sono una parte a sé: un problema lì non nasconde il resto del rapporto.
            try { Local = await gateway.Send<GeCoExternalCustomers>(new("ExternalCustomers"), lifetime.Token); LocalMessage = ""; }
            catch (Exception ex) when (ex is not OperationCanceledException) { LocalMessage = "Clienti di GeCa non letti: " + Message(ex); }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Error = Message(ex); }
        finally { Busy = false; Changed?.Invoke(); }
    }
    // Notifica push «GeCo» (anche gli esiti delle cartelle chieste): si ricarica il rapporto.
    private long seen;
    public async Task PushedAsync(long revision)
    {
        if (revision <= seen) return;
        seen = revision;
        if (Allowed && !Busy) await LoadAsync();
    }

    // ---------------------------------------------------------------- righe delle sezioni (testi già pronti)

    public sealed record Row(object Source, string A, string B, string C, string D, string E, string F, string G);
    public static readonly IReadOnlyDictionary<string, string[]> Columns = new Dictionary<string, string[]>
    {
        [Projects] = ["Codice", "Anno", "Descrizione e cliente", "Stato", "Situazione", "Cartella", "Note"],
        [Folders] = ["Cartella", "Categoria", "Committente", "Clienti proposti", "Da creare", "Candidata per", "Note"],
        [Customers] = ["Codice", "Ragione sociale", "Nome breve", "Proposta", "P.IVA / codice fiscale", "Commesse", "Da sistemare"],
        [Clients] = ["Committente", "Cartelle", "Cliente", "Nome breve", "Proposte", "Commessa", "Note"],
        [External] = ["Cliente di GeCa", "P.IVA / codice fiscale", "Esito", "Cliente di GeCo", "Commesse", "Indirizzo", "Motivo"],
        [Requests] = ["Data", "Elemento", "Cartella", "Stato", "Esito", "Chiesta da", "Eseguita da"],
    };

    public static string Situation(string s) => s switch
    {
        "Collegata" => "Collegata", "CollegataInArchivio" => "Collegata (in archivio)", "InArchivio" => "In archivio: da collegare", "DaCollegare" => "Da collegare",
        "DaRicollegare" => "Da ricollegare", "CartellaSparita" => "Cartella sparita", "SenzaCartella" => "Senza cartella", "PrimaDelleCartelle" => "Prima delle cartelle", _ => s,
    };
    private static string Level(string l) => l switch { "sicura" => "proposta sicura", "daverificare" => "proposta da verificare", _ => "" };
    public static string FolderName(GeCoFolder? f) => f == null ? "" : (f.Archiviata ? "[archivio] " : "") + f.Nome + (f.Presente ? "" : " (non più sul disco)");
    private static string Join(params string?[] parts) => string.Join(" – ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
    private static string Fiscal(string? vat, string? tax) => Join(vat, tax);
    private string CustomerName(int id) => Local?.GeCo.FirstOrDefault(c => c.Id == id) is { } c ? c.RagioneSociale + (c.NomeBreve is { Length: > 0 } nb ? " («" + nb + "»)" : "") : "#" + id;

    public IEnumerable<Row> Items()
    {
        var r = Report;
        IEnumerable<Row> rows = Section switch
        {
            Projects => (r?.Commesse ?? []).Where(c => (IncludeArchived || c.Stato == "Aperta") && (View == All || Fix.Contains(c.Situazione)))
                .Select(c => new Row(c, c.Codice, c.Anno?.ToString() ?? "", Join(c.Descrizione, c.Cliente), c.Stato, Situation(c.Situazione),
                    c.Cartella != null ? FolderName(c.Cartella) : c.Proposta != null ? FolderName(c.Proposta) + " (" + Level(c.Livello) + ")" : "",
                    Join(c.Motivo, c.Richiesta is { } q ? "cartella chiesta: " + q.StatoTesto.ToLowerInvariant() : null, c.PuoRichiedereCartella ? "si può chiedere la cartella" : null))),
            Folders => (r?.Cartelle ?? []).Where(f => IncludeArchived || !f.Cartella.Archiviata)
                .Select(f => new Row(f, FolderName(f.Cartella), f.Cartella.Categoria switch { "C" => "Commesse", "G" => "Gare", _ => "Archivio" }, f.Cartella.Committente ?? "",
                    string.Join("; ", f.Clienti.Take(3).Select(c => (c.NomeBreve ?? c.RagioneSociale) + " (" + Level(c.Livello) + ")")),
                    f.Creabile ?? "", string.Join(", ", f.CandidataPer.Select(e => e.Tipo + " " + e.Codice)), f.MotivoNoCreazione ?? "")),
            Customers => (r?.Clienti ?? []).Select(c => new Row(c, c.Codice, c.RagioneSociale ?? "", c.NomeBreve ?? "",
                c.Proposta is { } p ? $"«{p.NomeBreve}» ({Level(p.Livello)}, {p.Motivo})" : "", Fiscal(c.PartitaIva, c.CodiceFiscale), c.Commesse.ToString(),
                Join(string.Join("; ", c.Problemi), c.Doppioni.Length > 0 ? "doppione: " + string.Join(", ", c.Doppioni) : null))),
            Clients => (r?.CommittentiDiversi ?? []).Select(d => new Row(d, d.Cartella.Committente ?? "", "1", d.Cliente ?? "", d.NomeBreve, "", d.Codice,
                    "committente della cartella diverso dal nome breve del cliente della commessa"))
                .Concat((r?.CommittentiSenzaCliente ?? []).Select(u => new Row(u, u.Committente, u.Cartelle.ToString(), "", "",
                    string.Join("; ", u.Proposti.Take(3).Select(c => (c.NomeBreve ?? c.RagioneSociale) + " (" + Level(c.Livello) + ")")), "", "nessun cliente di GeCo con questo nome breve"))),
            External => (Local?.Clienti ?? []).Select(c => new Row(c, c.Nome, Fiscal(c.PartitaIva, c.CodiceFiscale), ExternalState(c),
                c.Collegato is int l ? CustomerName(l) : c.IdCliente is int a ? CustomerName(a) : string.Join("; ", c.Candidati.Take(3).Select(CustomerName)),
                c.Commesse.ToString(), c.Indirizzo ?? "", c.Motivo)),
            _ => (r?.Richieste ?? []).Select(q => new Row(q, q.DataRichiesta, (q.Tipo == "gara" ? "Gara " : "Commessa ") + q.Codice, q.Nome, q.StatoTesto, q.Esito ?? "",
                Join(q.Utente, q.Origine), Join(q.Esecutore, q.DataEsito))),
        };
        foreach (var word in Filter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            rows = rows.Where(x => new[] { x.A, x.B, x.C, x.D, x.E, x.F, x.G }.Any(t => t.Contains(word, StringComparison.CurrentCultureIgnoreCase)));
        return rows;
    }
    public static string ExternalState(GeCoExternalCustomer c) => c.Collegato != null ? "Collegato (confermato)" : c.Esito switch
    {
        "Abbinato" => "Abbinato (stessa P.IVA o codice fiscale)", "Proposta" => "Proposta da confermare", "Ambiguo" => "Ambiguo: più clienti di GeCo", _ => "Solo in GeCa: da importare",
    };

    public int SafeShortNames => Report?.NomiBreviSicuri.Length ?? 0;
    public string Summary
    {
        get
        {
            if (Report is not { } r) return "";
            int fix = r.Commesse.Count(c => c.Stato == "Aperta" && Fix.Contains(c.Situazione)), without = r.Commesse.Count(c => c.Situazione == "SenzaCartella" && c.Stato == "Aperta");
            int archived = r.Commesse.Count(c => c.Situazione is "CollegataInArchivio" or "InArchivio");
            int creatable = r.Cartelle.Count(f => f.Creabile != null && !f.Cartella.Archiviata);
            int open = r.Richieste.Count(q => q.Stato is "InAttesa" or "InCorso");
            int local = Local?.Clienti.Count(c => c.Collegato == null && c.Esito != "Abbinato") ?? 0;
            return $"{r.Commesse.Length} commesse: {fix} aperte da sistemare, di cui {without} senza cartella; {archived} con la cartella in archivio. "
                + $"{r.Cartelle.Length} cartelle senza commessa né gara ({creatable} da cui creare). {r.Clienti.Length} clienti di GeCo da sistemare, {SafeShortNames} nomi brevi proposti come sicuri. "
                + (Local != null ? $"{local} clienti di GeCa da abbinare o importare. " : "") + (open > 0 ? $"{open} cartelle chieste in attesa." : "");
        }
    }

    // ---------------------------------------------------------------- azioni (sempre ricontrollate dal servizio)

    private async Task<bool> Run(Func<Task<string>> action)
    {
        try { Busy = true; Changed?.Invoke(); Done = await action(); Error = ""; return true; }
        catch (Exception ex) when (ex is not OperationCanceledException) { Error = Message(ex); Done = ""; return false; }
        finally { Busy = false; await LoadAsync(); }
    }
    private async Task<T?> Query<T>(GeCoCommand command) where T : class
    {
        try { return await gateway.Send<T>(command, lifetime.Token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Error = Message(ex); Changed?.Invoke(); return null; }
    }

    // Conferma in blocco: si manda l'anteprima vista, il servizio scrive tutti i nomi o nessuno.
    public async Task<bool> ConfirmSafeShortNamesAsync()
    {
        var items = Report?.NomiBreviSicuri ?? [];
        if (items.Length == 0 || !Ask($"Confermare {items.Length} nomi brevi proposti come sicuri (ricavati dalle cartelle già collegate alle commesse del cliente)?")) return false;
        return await Run(async () => $"{await gateway.Send<int>(new("ConfirmShortNames", Allineamento: new(NomiBrevi: items)), lifetime.Token)} nomi brevi confermati.");
    }
    public async Task<bool> SetShortNameAsync(GeCoAlignmentCustomer c, string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || !Ask($"Impostare «{name}» come nome breve del cliente {c.RagioneSociale}? È la parte «Cliente» dei nomi delle cartelle nuove.")) return false;
        return await Run(async () => { await gateway.Send<int>(new("ConfirmShortNames", Allineamento: new(NomiBrevi: [new(c.Id, name)])), lifetime.Token); return $"Nome breve «{name}» impostato per {c.RagioneSociale}."; });
    }

    public Task<GeCoFromFolderProposal?> ProposalAsync(GeCoAlignmentFolder f) => Query<GeCoFromFolderProposal>(new("FromFolderProposal", IdCartella: f.Cartella.Id));
    public async Task<bool> CreateFromFolderAsync(GeCoAlignmentFolder f, GeCoNewFromFolder data)
    {
        var what = data.Tipo == "gara" ? $"la gara {f.Cartella.Codice}" : $"la commessa {data.Codice}";
        if (!Ask($"Creare in GeCo {what} «{data.Descrizione}» e collegarla alla cartella {f.Cartella.Nome}? La cartella sul disco non viene toccata.")) return false;
        return await Run(async () =>
        {
            var made = await gateway.Send<GeCoCreated>(new("CreateFromFolder", IdCartella: f.Cartella.Id, Allineamento: new(Nuovo: data)), lifetime.Token);
            return $"{(made.Tipo == "gara" ? "Gara" : "Commessa")} {made.Codice} creata in GeCo e collegata alla cartella {f.Cartella.Nome}.";
        });
    }

    public Task<GeCoFolderPreview?> PreviewAsync(GeCoFolderAsk ask) => Query<GeCoFolderPreview>(new("FolderPreview", Allineamento: new(Cartella: ask)));
    // now: crea subito dal servizio di GeCa invece di aspettarne il giro; in entrambi i casi la richiesta resta registrata con l'esito.
    public async Task<bool> RequestFolderAsync(GeCoFolderAsk ask, string name, bool now)
    {
        if (!Ask(now ? $"Creare ora la cartella «{name}» con il modello sul server? Nessuna cartella esistente viene modificata."
                     : $"Chiedere al servizio di GeCa la cartella «{name}»? Sarà creata con il modello e collegata alla scansione successiva.")) return false;
        return await Run(async () =>
        {
            var q = await gateway.Send<GeCoFolderRequest>(new("RequestFolder", Valore: now, Allineamento: new(Cartella: ask)), lifetime.Token);
            return q.Stato switch
            {
                "Creata" => $"Cartella «{q.Nome}» creata: sarà collegata alla commessa dopo la scansione (pochi secondi).",
                "Errore" or "Rifiutata" => $"Cartella «{q.Nome}» non creata: {q.Esito}",
                _ => $"Cartella «{q.Nome}» chiesta: {q.StatoTesto.ToLowerInvariant()}.",
            };
        });
    }
    public async Task<bool> CancelRequestAsync(GeCoFolderRequest q)
    {
        if (q.Stato != "InAttesa" || !Ask($"Annullare la richiesta della cartella «{q.Nome}»?")) return false;
        return await Run(async () => { await gateway.Send<GeCoFolderRequest>(new("CancelFolderRequest", Id: q.Id, Allineamento: new(Versione: q.Versione)), lifetime.Token); return "Richiesta annullata."; });
    }

    public async Task<bool> LinkCustomerAsync(GeCoExternalCustomer c, int? geCo)
    {
        if (!Ask(geCo is int id ? $"Collegare il cliente di GeCa «{c.Nome}» al cliente di GeCo {CustomerName(id)}? Le commesse che lo citano mostreranno il cliente di GeCo."
                                : $"Togliere il collegamento del cliente di GeCa «{c.Nome}»?")) return false;
        return await Run(async () =>
        {
            await gateway.Send<bool>(new("LinkCustomer", Allineamento: new(IdLocale: c.Id, IdCliente: geCo)), lifetime.Token);
            return geCo is int x ? $"«{c.Nome}» collegato a {CustomerName(x)}." : $"Collegamento di «{c.Nome}» tolto.";
        });
    }
    public async Task<bool> ImportCustomerAsync(GeCoCustomerImport data)
    {
        if (!Ask($"Inserire in GeCo il cliente «{data.RagioneSociale}» (P.IVA {data.PartitaIva})? Il cliente di GeCa diventerà quello di GeCo.")) return false;
        return await Run(async () =>
        {
            var made = await gateway.Send<GeCoCustomerSummary>(new("ImportCustomer", Allineamento: new(Cliente: data)), lifetime.Token);
            return $"Cliente {made.Codice} «{made.RagioneSociale}» inserito in GeCo.";
        });
    }
}
