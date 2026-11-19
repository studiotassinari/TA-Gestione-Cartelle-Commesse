using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Transport;
// Comandi applicativi espliciti, senza percorsi radice modificabili dal client.
// GeCo (dalla 2.37.0): pianificazione e ore lavorate di GeCo sul database comune, sempre con la versione 2 del protocollo;
// un servizio precedente risponde «Comando non supportato.» e il client chiede di aggiornarlo.
// SupportsGeCoEvents (dalla 2.38.0, solo con Subscribe): il client accetta anche le notifiche «GeCo» degli eventi di GeCo2027;
// un client precedente non lo dichiara e riceve soltanto «Changed» e «Heartbeat», che conosce.
// SupportsGeCoViews (dalla 2.39.0, solo con Subscribe): il client accetta anche le notifiche «GeCoViste» degli eventi tecnici
// (pianificazione e ore salvate in GeCo2027 o in un altro client di GeCa), con cui ricarica le finestre aperte.
public sealed record Request(string Operation, string Category = "C", string? FolderId = null, string? RelativePath = null, int Version = 2, CreateJobRequest? Creation = null, string? ClientMachine = null, string? ClientVersion = null, string? CorrelationId = null, bool SupportsAcknowledgements = false, AnalysisQuery? Analysis = null, FileSearchQuery? Files = null, DocumentQuery? Document = null, DocumentListQuery? Documents = null, string? McpTool = null, GeologyQuery? Geology = null, GeologicalReadQuery? GeologicalRead = null, GeologicalPositionUpdate? GeologicalPosition = null, GeologicalAnalysisQueueQuery? GeologicalPending = null, GeologicalAnalysisDraft? GeologicalAnalysis = null, GeologicalDossierQuery? GeologicalDossiers = null, GeologicalGeocodeQuery? GeologicalGeocode = null, IpripiQuery? Ipripi = null, IpripiReadQuery? IpripiRead = null, IpripiAnalysisDraft? IpripiAnalysis = null, BusinessCommand? Business = null, DeepInspectionQuery? DeepInspection = null, FolderArchiveQuery? FolderArchive = null, CurriculumCommand? Curriculum = null, GeCoCommand? GeCo = null, bool SupportsGeCoEvents = false, bool SupportsGeCoViews = false);
