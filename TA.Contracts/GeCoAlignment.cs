namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Allineamento tra GeCo e GeCa (dalla 2.40.0, database comune alla migrazione 14): commesse con le loro cartelle (comprese quelle
// in archivio), cartelle senza commessa, clienti con il nome breve delle cartelle e con P.IVA e codice fiscale, cartelle chieste
// da GeCo2027 o da GeCa. Rapporto, proposte e scritture sono quelli di ServizioAllineamento e ServizioRichiesteCartelle della
// libreria GeCo.Dati, gli stessi della pagina Allineamento di GeCo2027: qui ci sono soltanto i dati scambiati.
// Livello: "sicura", "daverificare" o "" (nessuna proposta). Le date sono gg/mm/aaaa hh:mm.

// Parametri dei comandi dell'allineamento (GeCoCommand.Allineamento). Quali campi servono dipende dall'azione.
public sealed record GeCoAlignmentRequest(GeCoShortName[]? NomiBrevi = null, GeCoNewFromFolder? Nuovo = null, GeCoFolderAsk? Cartella = null,
    GeCoCustomerImport? Cliente = null, string? IdLocale = null, int? IdCliente = null, int Versione = 0);

// Commessa e situazione delle sue cartelle. Situazione: Collegata, CollegataInArchivio, InArchivio, DaCollegare, DaRicollegare,
// CartellaSparita, SenzaCartella, PrimaDelleCartelle. Stato: Aperta, Chiusa, Archiviata.
public sealed record GeCoAlignmentProject(int Id, string Codice, int? Anno, string? Descrizione, int? IdCliente, string? Cliente, string? NomeBreve, string Stato,
    string Situazione, GeCoFolder? Cartella, GeCoFolder? Proposta, string Livello, string? Motivo, GeCoFolderRequest? Richiesta, bool PuoRichiedereCartella, string? MotivoNoRichiesta);
// Cliente proposto per un committente: sempre una proposta, con il livello e il motivo.
public sealed record GeCoProposedCustomer(int IdCliente, string? RagioneSociale, string? NomeBreve, string Livello, string Motivo);
// Cartella presente senza commessa né gara. Creabile: "commessa", "gara" o null (MotivoNoCreazione dice perché).
public sealed record GeCoAlignmentFolder(GeCoFolder Cartella, GeCoLinkElement[] CandidataPer, string? Creabile, string? MotivoNoCreazione, GeCoProposedCustomer[] Clienti);
public sealed record GeCoDifferentClient(int IdCommessa, string Codice, int IdCliente, string? Cliente, string NomeBreve, GeCoFolder Cartella);
public sealed record GeCoShortNameProposal(int IdCliente, string NomeBreve, string Livello, int Cartelle, int Totale, string[] Alternative, string Motivo);
// Cliente di GeCo da sistemare: senza nome breve, con P.IVA o codice fiscale mancanti o non validi (Problemi), doppio.
public sealed record GeCoAlignmentCustomer(int Id, string Codice, string? RagioneSociale, string? NomeBreve, string? PartitaIva, string? CodiceFiscale,
    string[] Problemi, GeCoShortNameProposal? Proposta, int Commesse, string[] Doppioni);
public sealed record GeCoDuplicateGroup(string Chiave, string Testo, int[] Clienti);
public sealed record GeCoUnknownClient(string Committente, int Cartelle, int[] IdCartelle, GeCoProposedCustomer[] Proposti);
public sealed record GeCoShortName(int IdCliente, string NomeBreve);
// Cartella chiesta a GeCa. Stato: InAttesa, InCorso, Creata, Errore, Rifiutata, Annullata. Versione: per annullare senza sovrapporsi.
public sealed record GeCoFolderRequest(int Id, string Tipo, int IdElemento, string Categoria, string Codice, string Nome, string Stato, string StatoTesto,
    string? Esito, string? Percorso, string DataRichiesta, string? Utente, string Origine, string? DataEsito, string? Esecutore, int Versione);
public sealed record GeCoAlignment(GeCoAlignmentProject[] Commesse, GeCoAlignmentFolder[] Cartelle, GeCoDifferentClient[] CommittentiDiversi,
    GeCoAlignmentCustomer[] Clienti, GeCoDuplicateGroup[] Doppioni, GeCoUnknownClient[] CommittentiSenzaCliente, GeCoFolderRequest[] Richieste,
    GeCoShortName[] NomiBreviSicuri, int? AnnoMinimo);

// Creazione di una commessa o di una gara da una cartella: dati proposti (da confermare e correggere) e dati confermati.
public sealed record GeCoChoice(int Id, string Testo);
public sealed record GeCoFromFolderProposal(string Tipo, GeCoFolder Cartella, string Codice, bool CodiceDellaCartella, string? AvvisoCodice, int Anno, string Descrizione,
    string? Luogo, GeCoProposedCustomer[] Clienti, int? IdResponsabile, GeCoChoice[] Responsabili, bool AreaObbligatoria, GeCoChoice[] Aree, string[] Blocchi);
// Tipo "commessa": Codice, Anno, Descrizione, IdCliente, IdResponsabile, IdAreaBusiness, Note. Tipo "gara": Descrizione = oggetto, IdCliente, Ente, Luogo.
public sealed record GeCoNewFromFolder(string Tipo, string Codice, int Anno, string Descrizione, int? IdCliente, int? IdResponsabile = null, int? IdAreaBusiness = null,
    string? Note = null, string? Ente = null, string? Luogo = null);
public sealed record GeCoCreated(string Tipo, int Id, string Codice);

// Cartella da chiedere per una commessa o una gara senza cartella: valori null = quelli proposti dalla libreria.
public sealed record GeCoFolderAsk(string Tipo, int Id, string? Cliente = null, string? Luogo = null, string? Oggetto = null);
// Anteprima del nome con le regole della creazione da modello (NomiCartelle): errori del nome, blocchi dell'elemento e avvisi.
public sealed record GeCoFolderPreview(string Tipo, int Id, string Descrizione, string Nome, string Codice, string Cliente, string? Luogo, string Oggetto,
    string[] Errori, string[] Blocchi, string[] Avvisi, string? NomeBreveCliente, bool PuoRichiedere, GeCoFolderRequest? Aperta);

// Clienti dell'archivio locale di GeCa rispetto a quelli di GeCo. Esito: Abbinato (stessa P.IVA o codice fiscale), Proposta (da
// confermare), Ambiguo (più clienti di GeCo), SoloEsterno (da importare in GeCo). Collegato: cliente di GeCo confermato da un
// responsabile autorizzato (prevale sull'esito calcolato).
public sealed record GeCoExternalCustomer(string Id, string Nome, string? PartitaIva, string? CodiceFiscale, string? Indirizzo, string? Email, string Esito,
    int? IdCliente, int[] Candidati, string Motivo, int? Collegato, int Commesse);
public sealed record GeCoCustomerSummary(int Id, string Codice, string RagioneSociale, string? NomeBreve, string? PartitaIva, string? CodiceFiscale, string? Localita);
public sealed record GeCoExternalCustomers(GeCoExternalCustomer[] Clienti, GeCoCustomerSummary[] GeCo);
// Cliente dell'archivio locale da inserire in GeCo: campi obbligatori della pagina Clienti di GeCo2027.
public sealed record GeCoCustomerImport(string IdLocale, string RagioneSociale, string? Indirizzo, string? Cap, string? Localita, string? Provincia,
    string? PartitaIva, string? CodiceFiscale, string? Email = null, string? Pec = null, string? NomeBreve = null);
