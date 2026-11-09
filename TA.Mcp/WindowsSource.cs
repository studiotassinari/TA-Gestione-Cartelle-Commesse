using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;

namespace TA.Mcp;

// Il contratto non offre comandi generici, scansioni, amministrazione o scrittura.
public interface IReadOnlySource
{
    Task<GeologyPage> Geology(GeologyQuery query,CancellationToken token) => throw new NotSupportedException();
    Task<DocumentPage> GeologicalDocument(GeologicalReadQuery query,CancellationToken token) => throw new NotSupportedException();
    Task<DocumentPage> Document(string category,DocumentQuery query,CancellationToken token) => throw new NotSupportedException();
    Task<DocumentListPage> Documents(string category,DocumentListQuery query,CancellationToken token) => throw new NotSupportedException();
    Task<CategoryState> State(string category, CancellationToken token);
    Task<FolderEntry[]> Children(string category, string id, string relative, CancellationToken token);
    Task<string> DailyReport(CancellationToken token);
    Task<AnalysisPage> Analysis(AnalysisQuery query, CancellationToken token);
    Task<System.Text.Json.JsonElement> DeepInspection(string category,DeepInspectionQuery query,CancellationToken token)=>throw new NotSupportedException();
    Task<AnalysisStatus> AnalysisStatus(CancellationToken token)=>throw new NotSupportedException();
    Task<ScanProgress> Progress(CancellationToken token);
    Task<FileSearchPage> Files(FileSearchQuery query, CancellationToken token);
}

// Usa l'identità Windows del processo; nessuna password viene raccolta o salvata.
public sealed class WindowsSource(string host, int port) : IReadOnlySource, IGeologyAnalysisSource, IIpripiSource
{
    private readonly ServerConnection connection = new(host, port);
    public Task<CategoryState> State(string category, CancellationToken token) =>
        Send<CategoryState>(new Request("State", category), token);
    public Task<FolderEntry[]> Children(string category, string id, string relative, CancellationToken token) =>
        Send<FolderEntry[]>(new Request("Children", category, id, relative), token);
    public Task<System.Text.Json.JsonElement> DeepInspection(string category,DeepInspectionQuery query,CancellationToken token)=>Send<System.Text.Json.JsonElement>(new Request("DeepInspection",category,DeepInspection:query),token);
    public Task<AnalysisStatus> AnalysisStatus(CancellationToken token)=>Send<AnalysisStatus>(new Request("AnalysisStatus"),token);
    public Task<ScanProgress> Progress(CancellationToken token) => Send<ScanProgress>(new Request("Progress"), token);
    public Task<FileSearchPage> Files(FileSearchQuery query, CancellationToken token) => Send<FileSearchPage>(new Request("Files", Files: query), token);
    public Task<AnalysisPage> Analysis(AnalysisQuery query, CancellationToken token) => Send<AnalysisPage>(new Request("Analysis", Analysis: query), token);
    public Task<string> DailyReport(CancellationToken token) =>
        Send<string>(new Request("DailyReport"), token);
    public Task<GeologyPage> Geology(GeologyQuery query,CancellationToken token) => Send<GeologyPage>(new Request("Geology",Geology:query), token);
    public Task<DocumentPage> GeologicalDocument(GeologicalReadQuery query,CancellationToken token) => Send<DocumentPage>(new Request("ReadGeologicalDocument",GeologicalRead:query), token);
    public Task<DocumentPage> Document(string category,DocumentQuery query,CancellationToken token) => Send<DocumentPage>(new Request("ReadDocument",category,Document:query), token);
    public Task<DocumentListPage> Documents(string category,DocumentListQuery query,CancellationToken token) => Send<DocumentListPage>(new Request("ListDocuments",category,Documents:query), token);
    public Task<GeologicalAnalysisQueue> PendingGeology(GeologicalAnalysisQueueQuery query, CancellationToken token) => Send<GeologicalAnalysisQueue>(new("PendingGeologicalAnalyses", GeologicalPending: query), token);
    public Task<GeologicalAnalysisRecord> SaveGeologicalAnalysis(GeologicalAnalysisDraft draft, CancellationToken token) => Send<GeologicalAnalysisRecord>(new("SaveGeologicalAnalysis", GeologicalAnalysis: draft), token);
    public Task<GeologicalDossierPage> GeologicalDossiers(GeologicalDossierQuery query, CancellationToken token) => Send<GeologicalDossierPage>(new("GeologicalDossiers", GeologicalDossiers: query), token);
    public Task<IpripiPage> Ipripi(IpripiQuery query, CancellationToken token) => Send<IpripiPage>(new("Ipripi", Ipripi: query), token);
    public Task<IpripiRules> IpripiRules(CancellationToken token) => Send<IpripiRules>(new("IpripiRules"), token);
    public Task<DocumentPage> ReadIpripi(IpripiReadQuery query, CancellationToken token) => Send<DocumentPage>(new("ReadIpripi", IpripiRead: query), token);
    public Task<IpripiAnalysisRecord> SaveIpripi(IpripiAnalysisDraft draft, CancellationToken token) => Send<IpripiAnalysisRecord>(new("SaveIpripi", IpripiAnalysis: draft), token);
    private Task<T> Send<T>(Request request, CancellationToken token) { var tagged = McpInvocation.Tag(request); return McpErrors.Run(() => connection.SendAsync<T>(tagged, token)); }
}
