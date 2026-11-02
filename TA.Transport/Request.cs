using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Transport;
// Comandi applicativi espliciti, senza percorsi radice modificabili dal client.
public sealed record Request(string Operation, string Category = "C", string? FolderId = null, string? RelativePath = null, int Version = 2, CreateJobRequest? Creation = null, string? ClientMachine = null, string? ClientVersion = null, string? CorrelationId = null, bool SupportsAcknowledgements = false, AnalysisQuery? Analysis = null, FileSearchQuery? Files = null, DocumentQuery? Document = null, DocumentListQuery? Documents = null, string? McpTool = null, GeologyQuery? Geology = null, GeologicalReadQuery? GeologicalRead = null);
