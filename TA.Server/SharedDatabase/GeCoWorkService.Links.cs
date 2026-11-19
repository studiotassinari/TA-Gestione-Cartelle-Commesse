using GeCo.Dati;
using GeCo.Dati.Regole;
using Microsoft.EntityFrameworkCore;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server.SharedDatabase;

// Collegamento definitivo delle cartelle a commesse e gare (pagina Collegamento cartelle di GeCo2027) e notifiche degli eventi
// di GeCo2027. Proposte, livelli e scritture sono di ServizioCollegamentiCartelle con origine «GeCa»: l'evento registrato dice
// a GeCo2027 che il collegamento è stato deciso qui. La situazione completa e le scritture sono riservate agli amministratori
// di GeCo, decisi dalla libreria (Ruoli) sull'utente del token Windows; le notifiche le vede ciascuno secondo le regole di
// visibilità comuni (società e aree di business della commessa citata), come nella campanella di GeCo2027.
public sealed partial class GeCoWorkService
{
    public const string LinksNotReady = "Collegamento delle cartelle non disponibile: il database comune non è ancora aggiornato (migrazione 12). Aggiornare prima GeCo2027 o riavviare il servizio.";
    public const string EventsNotReady = "Notifiche di GeCo2027 non disponibili: il database comune non è ancora aggiornato (migrazione 12).";
    // Notifiche restituite al massimo per richiesta: le più recenti, come l'elenco della campanella di GeCo2027.
    public const int RecentEvents = 50;
    private const int EventWindow = 200;

    // Notifiche per i client di GeCa: gli eventi di GeCo2027 e, dalla 2.40.0, gli esiti delle cartelle chieste scritti da GeCa
    // (CartellaCreata e CartellaNonCreata, con i destinatari di NotificheMirate: li vede soltanto chi aveva chiesto la cartella).
    // Gli altri eventi di GeCa (scansioni, collegamenti decisi qui) sono già noti a chi li ha fatti e non diventano notifiche.
    public static bool Notice(string origin, string type) => origin == EventiSincronizzazione.GeCo2027
        || origin == EventiSincronizzazione.GeCa && type is EventiSincronizzazione.Tipi.CartellaCreata or EventiSincronizzazione.Tipi.CartellaNonCreata;
    private static IQueryable<tblSincronizzazioneEventi> Notices(GeCoDbContext db) => EventiSincronizzazione.SenzaTecnici(db.tblSincronizzazioneEventi.AsNoTracking())
        .Where(e => e.Sev_Origine == EventiSincronizzazione.GeCo2027 || e.Sev_Origine == EventiSincronizzazione.GeCa
            && (e.Sev_Tipo == EventiSincronizzazione.Tipi.CartellaCreata || e.Sev_Tipo == EventiSincronizzazione.Tipi.CartellaNonCreata));

    private static async Task<object> HandleLinksAsync(GeCoCommand c, ContestoRegole context, CancellationToken token)
    {
        if (c.Action == "SyncEvents") return await EventsAsync(context, c.DopoId, token);
        if (c.Action == "ViewEvents") return await ViewEventsAsync(context, c.DopoId, token);
        // Prima il ruolo, poi lo stato del database: a chi non è amministratore non serve sapere altro.
        Ruoli.RichiediAmministratore(context.Utente);
        if (context.VersioneDatabase < EventiSincronizzazione.VersioneRichiesta) throw new IOException(LinksNotReady);
        var links = new ServizioCollegamentiCartelle(context, EventiSincronizzazione.GeCa);
        switch (c.Action)
        {
            case "Links":
                var syncs = (await context.Db.tblCartelleSincronizzazioni.AsNoTracking().OrderBy(x => x.Cas_Categoria).ToListAsync(token))
                    .Select(x => new GeCoFolderSync(x.Cas_Categoria, Date(x.Cas_Data), x.Cas_Origine, x.Cas_Cartelle)).ToArray();
                return Map(await links.CalcolaAsync(stop: token), syncs);
            case "Folders": return await FoldersAsync(links, c.Testo, c.Tipo, token);
            case "Link": return (await links.CollegaAsync(c.Tipo ?? "", c.Id, c.IdCartella, c.Valore, token)).IdRiga;
            case "Unlink": return await links.ScollegaAsync(c.Tipo ?? "", c.Id, token);
            default:
                // «Conferma tutte le proposte sicure»: la libreria ricalcola e scrive tutte o nessuna.
                var items = (c.Collegamenti ?? []).Select(v => new VoceCollegamento(v.Tipo, v.Id, v.IdCartella)).ToList();
                return await links.ConfermaSicureAsync(items, token);
        }
    }

    private static string Date(DateTime value) => value.ToString("dd/MM/yyyy HH:mm");
    public static GeCoFolder? Folder(CartellaCollegabile? c) => c is null ? null
        : new(c.Id, c.Percorso, EventiSincronizzazione.NomeCartella(c.Percorso), c.Categoria, c.Codice, c.Anno, c.Committente, c.Oggetto, c.Presente,
            c.UltimaRilevazione is { } d ? Date(d) : null, c.InArchivio);
    private static GeCoLinkConflict Conflict(ConflittoCollegamento x) => new(x.Tipo, x.TipoElemento, x.IdElemento, x.IdCartella, x.IdCartellaNuova, x.Testo);
    public static string Level(LivelloProposta level) => level switch { LivelloProposta.Sicura => "sicura", LivelloProposta.DaVerificare => "daverificare", _ => "" };

    // Esito delle regole nei record del protocollo, con le cartelle già risolte (come l'elenco di CollegamentiCartelleController).
    public static GeCoLinks Map(EsitoCollegamenti e, GeCoFolderSync[] syncs)
    {
        GeCoFolder? F(int? id) => id is int i ? Folder(e.Cartelle.GetValueOrDefault(i)) : null;
        GeCoFolderCandidate Candidate(CandidatoCartella c) => new(c.IdCartella, c.Punteggio, c.Motivo, F(c.IdCartella));
        var rows = e.Elementi.Select(r => new GeCoLinkRow(r.Elemento.Tipo, r.Elemento.Id, r.Elemento.Codice, r.Anno, r.Descrizione, r.Cliente, r.Sottocommessa,
            r.Collegamento is { } l ? new GeCoLinkInfo(l.IdRiga, l.IdCartella, Date(l.Data), l.Origine, l.Utente) : null, F(r.Collegamento?.IdCartella),
            r.Proposta is { } p ? Candidate(p) : null, r.Candidati.Select(Candidate).ToArray(), Level(r.Livello), r.Conflitti.Select(Conflict).ToArray())).ToArray();
        var free = e.CartelleLibere.Select(l => new GeCoFreeFolder(F(l.IdCartella)!, l.CandidataPer.Select(x => new GeCoLinkElement(x.Tipo, x.Id, x.Codice)).ToArray())).ToArray();
        return new(rows, free, e.Conflitti.Select(Conflict).ToArray(), e.Sicure.Select(v => new GeCoLinkItem(v.Tipo, v.Id, v.IdCartella)).ToArray(), syncs);
    }

    // Cartelle presenti per «Scegli cartella…»: tutte le parole cercate nel codice, nel percorso, nel committente, nell'oggetto o
    // nell'anno, solo della categoria ammessa per l'elemento; le più recenti prima, al massimo 200 (come in GeCo2027).
    private static async Task<GeCoFolderChoice[]> FoldersAsync(ServizioCollegamentiCartelle links, string? text, string? type, CancellationToken token)
    {
        var t = string.IsNullOrWhiteSpace(type) ? null : CollegamentiCartelle.ControllaTipo(type);
        var folders = (await links.CartelleAsync(token)).Where(c => c.Presente && (t is null || CollegamentiCartelle.CategoriaAmmessa(t, c.Categoria)));
        foreach (var word in (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            folders = folders.Where(c => new[] { c.Codice, c.Percorso, c.Committente, c.Oggetto, c.Anno?.ToString() }.Any(x => x != null && x.Contains(word, StringComparison.CurrentCultureIgnoreCase)));
        var linked = (await links.CollegamentiAsync(token)).GroupBy(l => l.IdCartella).ToDictionary(g => g.Key, g => g.First());
        return folders.OrderByDescending(c => c.Anno ?? 0).ThenByDescending(c => c.Codice, StringComparer.OrdinalIgnoreCase).Take(200)
            .Select(c => new GeCoFolderChoice(Folder(c)!, linked.TryGetValue(c.Id, out var l)
                ? new GeCoLinkElement(l.IdGara is not null ? CollegamentiCartelle.Gara : CollegamentiCartelle.Commessa, l.IdGara ?? l.IdCommessa ?? 0, "") : null))
            .ToArray();
    }

    // Eventi scritti da GeCo2027 (commesse e gare create, modificate, eliminate; collegamenti decisi là; richieste di accesso) dopo
    // DopoId, o gli ultimi se DopoId manca; sempre entro gli ultimi 200, così un client rimasto spento a lungo non scarica tutto il
    // registro. Solo notifiche: gli eventi tecnici di pianificazione e ore stanno fuori (SenzaTecnici, LeggiDopoAsync con
    // tecnici: false) e arrivano alle finestre con ViewEvents. Le notifiche con destinatari (richieste di accesso, migrazione 13)
    // le vedono soltanto i destinatari (NotificheMirate), le altre chi vede la commessa.
    // UltimoId è l'ultima notifica di GeCo2027 anche se l'utente non la vede: il client non la richiede di nuovo.
    private static async Task<GeCoSyncEvents> EventsAsync(ContestoRegole context, long? after, CancellationToken token)
    {
        if (context.VersioneDatabase < EventiSincronizzazione.VersioneRichiesta) return new([], 0, false, EventsNotReady);
        var db = context.Db;
        var recent = await Notices(db).OrderByDescending(e => e.Sev_Id).Select(e => e.Sev_Id).Take(EventWindow).ToListAsync(token);
        if (recent.Count == 0) return new([], 0, true);
        var from = Math.Max(after ?? 0, recent[^1] - 1);
        var events = (await Notices(db).Where(e => e.Sev_Id > from).OrderBy(e => e.Sev_Id).Take(EventWindow).ToListAsync(token))
            .Select(r => new EventoSincronizzazione(r.Sev_Id, r.Sev_Data, r.Sev_Origine, r.Sev_Tipo, r.Sev_IdCommessa, r.Sev_IdGara, r.Sev_IdCartella,
                r.Sev_Codice, r.Sev_Percorso, r.Sev_Testo, r.Sev_IdUtente, r.Sev_Utente)).ToList();
        var visibility = await EventiSincronizzazione.VisibilitaAsync(db, context.Utente, context.IdSocieta, token);
        var projects = await EventiSincronizzazione.CommesseAsync(db, events, token);
        var recipients = await NotificheMirate.DestinatariAsync(db, context.VersioneDatabase, events.Select(e => e.Id), token);
        var visible = events.Where(e => NotificheMirate.Visibile(e, projects, visibility, recipients)).OrderByDescending(e => e.Id).Take(RecentEvents)
            .Select(e => new GeCoSyncEvent(e.Id, Date(e.Data), e.Origine, e.Tipo, e.IdCommessa, e.IdGara, e.IdCartella, e.Codice, e.Percorso, e.Testo, e.Utente)).ToArray();
        return new(visible, recent[0], true);
    }

    // Pianificazione e ore salvate (eventi tecnici di GeCo2027 e di GeCa) dopo DopoId, per le finestre aperte del client, solo
    // quelli che l'utente può vedere: la propria persona, la pianificazione di tutti per amministratori e team manager, le ore
    // di tutti per chi le vede (regole della libreria, EventiSincronizzazione.Visibile). Senza DopoId solo l'ultimo Id (punto di
    // partenza del client). Al massimo gli ultimi 200: chi è rimasto più indietro riceve Parziale e ricarica comunque.
    private static async Task<GeCoViewUpdates> ViewEventsAsync(ContestoRegole context, long? after, CancellationToken token)
    {
        if (context.VersioneDatabase < EventiSincronizzazione.VersioneRichiesta) return new([], 0, false);
        var db = context.Db;
        var recent = await db.tblSincronizzazioneEventi.AsNoTracking().Where(e => e.Sev_Codice == EventiSincronizzazione.MarcaTecnica)
            .OrderByDescending(e => e.Sev_Id).Select(e => e.Sev_Id).Take(EventWindow).ToListAsync(token);
        if (recent.Count == 0 || after is not long from || from >= recent[0]) return new([], recent.Count == 0 ? 0 : recent[0], true);
        bool partial = recent.Count == EventWindow && from < recent[^1] - 1;
        var events = await EventiSincronizzazione.LeggiDopoAsync(db, Math.Max(from, recent[^1] - 1), massimo: EventWindow, stop: token, tecnici: true);
        var visibility = await EventiSincronizzazione.VisibilitaAsync(db, context.Utente, context.IdSocieta, token);
        var updates = events.Where(e => EventiSincronizzazione.Visibile(e, new Dictionary<int, InfoCommessaEvento>(), visibility))
            .Select(e => (Event: e, Data: EventiSincronizzazione.Aggiornamento(e))).Where(x => x.Data != null)
            .Select(x => new GeCoViewUpdate(x.Event.Id, x.Event.Origine, x.Event.Tipo, x.Data!.IdCdc, DateRegole.Iso(x.Data.Dal), DateRegole.Iso(x.Data.Al),
                x.Data.Commesse.ToArray(), x.Data.AltreCommesse, x.Data.Scheda, x.Event.Utente)).ToArray();
        return new(updates, recent[0], true, partial);
    }
}
