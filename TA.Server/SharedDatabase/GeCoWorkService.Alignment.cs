using GeCo.Dati.Regole;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server.SharedDatabase;

// Archivio locale dei clienti di GeCa (gestionale del servizio), per l'abbinamento con i clienti di GeCo nel modulo Allineamento.
public interface ICustomerArchive
{
    Customer[] LocalCustomers();
    IReadOnlyDictionary<string, int> CustomerLinks();
    IReadOnlyDictionary<string, int> CustomerProjects();
    void LinkCustomer(string localId, int? geCoId, string actor);
    void CustomersChanged();
}

// Modulo «Allineamento» (2.40.0, pagina Allineamento di GeCo2027): rapporto, nomi brevi, commesse e gare create da cartelle,
// cartelle chieste per commesse senza cartella, clienti di GeCa da abbinare o importare in GeCo. Tutto con ServizioAllineamento
// e ServizioRichiesteCartelle con origine «GeCa»: le regole, i controlli e il ruolo di amministratore di GeCo (Ruoli, sull'utente
// del token Windows) sono quelli della libreria, mai riscritti qui. Le proposte restano proposte: si scrive solo su conferma.
public sealed partial class GeCoWorkService
{
    public const string AlignmentNotReady = "Allineamento non disponibile: il database comune non è ancora aggiornato (migrazione 14). Aggiornare prima GeCo2027 o riavviare il servizio.";
    // Esecuzione delle cartelle chieste e archivio locale dei clienti: assenti nelle verifiche che non li usano.
    public GeCoFolderRequestsService? FolderRequests { get; init; }
    public ICustomerArchive? Customers { get; init; }
    // Dopo un'importazione il catalogo dei clienti di GeCo si rilegge (lo stesso oggetto usato dal gestionale).
    public GeCoCustomerSource? CustomerSource { get; init; }

    public static bool IsAlignment(string action) => action is "Alignment" or "ConfirmShortNames" or "FromFolderProposal" or "CreateFromFolder" or "FolderPreview"
        or "RequestFolder" or "CancelFolderRequest" or "ExternalCustomers" or "LinkCustomer" or "ImportCustomer";

    private async Task<object> HandleAlignmentAsync(GeCoCommand c, ContestoRegole context, CancellationToken token)
    {
        // Prima il ruolo, poi lo stato del database, come per il collegamento delle cartelle.
        Ruoli.RichiediAmministratore(context.Utente);
        if (!ServizioAllineamento.Disponibile(context.VersioneDatabase)) throw new IOException(AlignmentNotReady);
        var a = c.Allineamento ?? new();
        var alignment = new ServizioAllineamento(context, EventiSincronizzazione.GeCa);
        var requests = new ServizioRichiesteCartelle(context, EventiSincronizzazione.GeCa);
        switch (c.Action)
        {
            case "Alignment": return Map(await alignment.RapportoAsync(token));
            case "ConfirmShortNames":
                return await alignment.ConfermaNomiBreviAsync((a.NomiBrevi ?? []).Select(v => new VoceNomeBreve(v.IdCliente, v.NomeBreve)).ToList(), token);
            case "FromFolderProposal": return Map(await alignment.PropostaAsync(c.IdCartella, token));
            case "CreateFromFolder":
                var n = a.Nuovo ?? throw new ArgumentException("Dati della commessa mancanti.");
                var made = n.Tipo == CollegamentiCartelle.Gara
                    ? await alignment.CreaGaraAsync(c.IdCartella, new DatiGaraDaCartella(n.Descrizione, n.IdCliente, n.Ente, n.Luogo), token)
                    : await alignment.CreaCommessaAsync(c.IdCartella, new DatiCommessaDaCartella(n.Codice, n.Anno, n.Descrizione,
                        n.IdCliente ?? throw new ArgumentException("Cliente non specificato"), n.IdResponsabile ?? throw new ArgumentException("Responsabile di commessa non specificato"),
                        n.IdAreaBusiness, n.Note), token);
                return new GeCoCreated(made.Tipo, made.Id, made.Codice);
            case "FolderPreview":
                var p = a.Cartella ?? throw new ArgumentException("Commessa o gara mancante.");
                return Map(await requests.AnteprimaAsync(p.Tipo, p.Id, p.Cliente, p.Luogo, p.Oggetto, token));
            case "RequestFolder":
                // Valore: creare subito, senza aspettare il giro del servizio. La richiesta resta comunque registrata, con il suo esito.
                var q = a.Cartella ?? throw new ArgumentException("Commessa o gara mancante.");
                var asked = await requests.RichiediAsync(q.Tipo, q.Id, q.Cliente, q.Luogo, q.Oggetto, token);
                if (FolderRequests is { } runner)
                {
                    if (c.Valore) await runner.RunOneAsync(asked.Id, token); else runner.Wake();
                }
                return Map((await requests.ElencoAsync(stop: token)).FirstOrDefault(r => r.Id == asked.Id) ?? asked);
            case "CancelFolderRequest": return Map(await requests.AnnullaAsync(c.Id, a.Versione, token));
            case "ExternalCustomers": return await ExternalCustomersAsync(context, token);
            case "LinkCustomer":
                var archive = Customers ?? throw new IOException("Archivio locale dei clienti non disponibile sul server.");
                if (a.IdCliente is int target && !(await ServizioAllineamento.ClientiGeCoAsync(context.Db, context.VersioneDatabase, context.IdSocieta, token)).Any(x => x.Id == target))
                    throw new KeyNotFoundException("Cliente di GeCo non trovato (forse eliminato).");
                archive.LinkCustomer(a.IdLocale ?? throw new ArgumentException("Cliente locale mancante."), a.IdCliente, Author(context));
                archive.CustomersChanged();
                return true;
            default:
                var i = a.Cliente ?? throw new ArgumentException("Cliente da importare mancante.");
                var imported = await alignment.ImportaClienteAsync(new NuovoClienteGeCo(i.RagioneSociale, i.Indirizzo, i.Cap, i.Localita, i.Provincia, i.PartitaIva,
                    i.CodiceFiscale, i.Email, i.Pec, i.NomeBreve), token);
                // Il cliente locale da cui nasce l'importazione diventa il cliente di GeCo appena creato.
                if (Customers is { } local && !string.IsNullOrEmpty(i.IdLocale)) { local.LinkCustomer(i.IdLocale, imported.Id, Author(context)); local.CustomersChanged(); }
                CustomerSource?.Invalidate();
                return Summary(imported);
        }
    }

    private static string Author(ContestoRegole context) => string.IsNullOrWhiteSpace(context.Utente.Ute_Descrizione) ? context.Utente.Ute_Username : context.Utente.Ute_Descrizione.Trim();

    // Clienti dell'archivio locale di GeCa con l'esito dell'abbinamento a quelli di GeCo (regole della libreria) e i collegamenti già
    // confermati. Nulla viene scritto: Abbinato vale da solo, Proposta e Ambiguo aspettano la scelta, SoloEsterno l'importazione.
    private async Task<GeCoExternalCustomers> ExternalCustomersAsync(ContestoRegole context, CancellationToken token)
    {
        var archive = Customers ?? throw new IOException("Archivio locale dei clienti non disponibile sul server.");
        var geco = await ServizioAllineamento.ClientiGeCoAsync(context.Db, context.VersioneDatabase, context.IdSocieta, token);
        var local = archive.LocalCustomers();
        var links = archive.CustomerLinks();
        var projects = archive.CustomerProjects();
        var ids = geco.Select(x => x.Id).ToHashSet();
        var rows = ServizioAllineamento.AbbinaClientiEsterni(local.Select(GeCoCustomerSource.ToExternal).ToList(), geco)
            .Select(m => new GeCoExternalCustomer(m.Cliente.Id, m.Cliente.Nome, m.Cliente.PartitaIva, m.Cliente.CodiceFiscale, m.Cliente.Indirizzo, m.Cliente.Email,
                m.Esito.ToString(), m.IdCliente, m.Candidati.ToArray(), m.Motivo, links.TryGetValue(m.Cliente.Id, out var l) && ids.Contains(l) ? l : null,
                projects.GetValueOrDefault(m.Cliente.Id)))
            .OrderBy(r => r.Nome, StringComparer.CurrentCultureIgnoreCase).ToArray();
        return new(rows, geco.Select(Summary).ToArray());
    }

    private static GeCoCustomerSummary Summary(ClienteGeCo c) => new(c.Id, c.Codice, c.RagioneSociale, c.NomeBreve, c.PartitaIva, c.CodiceFiscale, c.Localita);

    // ---------------------------------------------------------------- dai record della libreria a quelli del protocollo

    public static GeCoFolderRequest Map(RichiestaCartella r) => new(r.Id, r.Tipo, r.IdElemento, r.Categoria, r.Codice, r.Nome, r.Stato, r.StatoTesto, r.Esito, r.Percorso,
        Date(r.DataRichiesta), r.Utente, r.Origine, r.DataEsito is { } d ? Date(d) : null, r.Esecutore, r.Versione);
    private static GeCoProposedCustomer Map(ClienteProposto p) => new(p.IdCliente, p.RagioneSociale, p.NomeBreve, Level(p.Livello), p.Motivo);

    public static GeCoAlignment Map(RapportoAllineamento r) => new(
        r.Commesse.Select(c => new GeCoAlignmentProject(c.Id, c.Codice, c.Anno, c.Descrizione, c.IdCliente, c.Cliente, c.NomeBreve, c.Stato.ToString(), c.Situazione.ToString(),
            Folder(c.Cartella), Folder(c.Proposta), Level(c.Livello), c.Motivo, c.Richiesta is { } q ? Map(q) : null, c.PuoRichiedereCartella, c.MotivoNoRichiesta)).ToArray(),
        r.Cartelle.Select(c => new GeCoAlignmentFolder(Folder(c.Cartella)!, c.CandidataPer.Select(x => new GeCoLinkElement(x.Tipo, x.Id, x.Codice)).ToArray(),
            c.Creabile, c.MotivoNoCreazione, c.Clienti.Select(Map).ToArray())).ToArray(),
        r.CommittentiDiversi.Select(d => new GeCoDifferentClient(d.IdCommessa, d.Codice, d.IdCliente, d.Cliente, d.NomeBreve, Folder(d.Cartella)!)).ToArray(),
        r.Clienti.Select(c => new GeCoAlignmentCustomer(c.Id, c.Codice, c.RagioneSociale, c.NomeBreve, c.PartitaIva, c.CodiceFiscale, c.Fiscale.Problemi.ToArray(),
            c.Proposta is { } p ? new GeCoShortNameProposal(p.IdCliente, p.NomeBreve, Level(p.Livello), p.Cartelle, p.Totale, p.Alternative.ToArray(), p.Motivo) : null,
            c.Commesse, c.Doppioni.ToArray())).ToArray(),
        r.Doppioni.Select(g => new GeCoDuplicateGroup(g.Chiave, g.Testo, g.Clienti.ToArray())).ToArray(),
        r.CommittentiSenzaCliente.Select(x => new GeCoUnknownClient(x.Committente, x.Cartelle, x.IdCartelle.ToArray(), x.Proposti.Select(Map).ToArray())).ToArray(),
        r.Richieste.Select(Map).ToArray(),
        r.NomiBreviSicuri.Select(v => new GeCoShortName(v.IdCliente, v.NomeBreve)).ToArray(),
        r.AnnoMinimo);

    public static GeCoFromFolderProposal Map(PropostaDaCartella p) => new(p.Tipo, Folder(p.Cartella)!, p.Codice, p.CodiceDellaCartella, p.AvvisoCodice, p.Anno, p.Descrizione,
        p.Luogo, p.Clienti.Select(Map).ToArray(), p.IdResponsabile, p.Responsabili.Select(o => new GeCoChoice(o.Id, o.Testo)).ToArray(), p.AreaObbligatoria,
        p.Aree.Select(o => new GeCoChoice(o.Id, o.Testo)).ToArray(), p.Blocchi.ToArray());

    public static GeCoFolderPreview Map(AnteprimaCartella a) => new(a.Tipo, a.Id, a.Descrizione, a.Nome.Nome, a.Nome.Codice, a.Nome.Cliente, a.Nome.Luogo, a.Nome.Oggetto,
        a.Nome.Errori.ToArray(), a.Blocchi.ToArray(), a.Avvisi.ToArray(), a.NomeBreveCliente, a.PuoRichiedere, a.Aperta is { } q ? Map(q) : null);
}
