using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;

namespace TA.Mcp;

public sealed class LocalSource : IReadOnlySource
{
    private readonly LocalPipeConnection connection = new();
    public Task<CategoryState> State(string category, CancellationToken token) => Send<CategoryState>(new Request("State", category), token);
    public Task<FolderEntry[]> Children(string category, string id, string relative, CancellationToken token) => Send<FolderEntry[]>(new Request("Children", category, id, relative), token);
    public Task<ScanProgress> Progress(CancellationToken token) => Send<ScanProgress>(new Request("Progress"), token);
    public Task<FileSearchPage> Files(FileSearchQuery query, CancellationToken token) => Send<FileSearchPage>(new Request("Files", Files: query), token);
    public Task<AnalysisPage> Analysis(AnalysisQuery query, CancellationToken token) => Send<AnalysisPage>(new Request("Analysis", Analysis: query), token);
    public Task<string> DailyReport(CancellationToken token) => Send<string>(new Request("DailyReport"), token);
    public Task<GeologyPage> Geology(GeologyQuery query,CancellationToken token) => Send<GeologyPage>(new Request("Geology",Geology:query), token);
    public Task<DocumentPage> GeologicalDocument(GeologicalReadQuery query,CancellationToken token) => Send<DocumentPage>(new Request("ReadGeologicalDocument",GeologicalRead:query), token);
    public Task<DocumentPage> Document(string category,DocumentQuery query,CancellationToken token) => Send<DocumentPage>(new Request("ReadDocument",category,Document:query), token);
    public Task<DocumentListPage> Documents(string category,DocumentListQuery query,CancellationToken token) => Send<DocumentListPage>(new Request("ListDocuments",category,Documents:query), token);
    private Task<T> Send<T>(Request request, CancellationToken token) { var tagged = McpInvocation.Tag(request); return McpErrors.Run(() => connection.SendAsync<T>(tagged, token)); }
}
