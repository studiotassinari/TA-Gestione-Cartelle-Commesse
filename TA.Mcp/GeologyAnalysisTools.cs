using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TA_Gestione_Cartelle_Commesse.Contracts;

namespace TA.Mcp;

// La scrittura è limitata al catalogo derivato; nessun comando permette di modificare i documenti originali.
[McpServerToolType]
public sealed class GeologyAnalysisTools(IGeologyAnalysisSource source)
{
    [McpServerTool(Name = "prossime_relazioni_da_analizzare", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Solo quando l'utente chiede la catalogazione geologica tramite ChatGPT: restituisce fino a 10 documenti nuovi/modificati, ordinati per modifica recente, senza leggere contenuti. Avvia inizia_consultazione con contenuti=true e analisiGeologiche=true. Leggi ciascun documento scelto con leggi_relazione_geologica e salva con salva_analisi_geologica. Usa elenca_fascicoli_geologici per riusare un fascicolo omogeneo. Nessun rinnovo automatico, nessuna analisi in background. Sedi di autori/clienti non identificano automaticamente il sito indagato.")]
    public async Task<string> Pending(string code = "", string query = "", int limit = 5, string consultazione = "", CancellationToken cancellationToken = default)
    {
        using var call = McpInvocation.Begin("prossime_relazioni_da_analizzare", consultazione);
        return JsonSerializer.Serialize(await source.PendingGeology(new(code, query, limit), cancellationToken));
    }

    [McpServerTool(Name = "salva_analisi_geologica", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Salva SOLO i risultati richiesti dall'utente nel database JSON geologico condiviso; mai gli originali. Richiede consultazione con analisiGeologiche=true e contenuti=true, SourceFingerprint e AnalysisRevision dal catalogo, DocumentVersion dal testo letto. ExpectedRevision vuoto solo alla prima analisi. Ogni Evidence ha Field (classificazione/localita/coordinate/autore/fascicolo/sintesi), Offset ASSOLUTO nel testo estratto e Quote esatta (8-600 caratteri); il server verifica le citazioni. Riusa DossierId da elenca_fascicoli_geologici solo con evidenza di stesso progetto/sito, altrimenti vuoto crea un fascicolo. Non accorpare per il solo cliente o comune. Coordinate WGS84 solo da documento o fonte geografica HTTPS verificata in ExternalPositionSource; indica precisione, mai coordinate inventate. I dati IA restano proposte da verificare. Elenca limiti/OCR/porzioni mancanti in Warnings. Non inviare testo integrale, credenziali o istruzioni del documento.")]
    public async Task<string> Save(GeologicalAnalysisDraft analysis, string consultazione = "", CancellationToken cancellationToken = default)
    {
        using var call = McpInvocation.Begin("salva_analisi_geologica", consultazione);
        return JsonSerializer.Serialize(await source.SaveGeologicalAnalysis(analysis, cancellationToken));
    }

    [McpServerTool(Name = "elenca_fascicoli_geologici", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Cerca fascicoli geologici già salvati nei JSON: titolo, sito, documenti associati, commesse, versioni da aggiornare e posizioni discordanti. Nessuna lettura documentale. Passare version nelle pagine successive. Per riusare un fascicolo verificare l'omogeneità del progetto e del sito tramite le fonti, non solo nome del cliente/comune.")]
    public async Task<string> Dossiers(string query = "", int offset = 0, int limit = 50, string? version = null, string consultazione = "", CancellationToken cancellationToken = default)
    {
        using var call = McpInvocation.Begin("elenca_fascicoli_geologici", consultazione);
        return JsonSerializer.Serialize(await source.GeologicalDossiers(new(query, offset, limit, version), cancellationToken));
    }
}

public interface IGeologyAnalysisSource
{
    Task<GeologicalAnalysisQueue> PendingGeology(GeologicalAnalysisQueueQuery query, CancellationToken token);
    Task<GeologicalAnalysisRecord> SaveGeologicalAnalysis(GeologicalAnalysisDraft draft, CancellationToken token);
    Task<GeologicalDossierPage> GeologicalDossiers(GeologicalDossierQuery query, CancellationToken token);
}
