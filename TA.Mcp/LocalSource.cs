using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;

namespace TA.Mcp;

public sealed class LocalSource : IReadOnlySource
{
    private readonly LocalPipeConnection connection = new();
    public Task<CategoryState> State(string category, CancellationToken token) => connection.SendAsync<CategoryState>(McpInvocation.Tag(new Request("State", category)), token);
    public Task<FolderEntry[]> Children(string category, string id, string relative, CancellationToken token) => connection.SendAsync<FolderEntry[]>(McpInvocation.Tag(new Request("Children", category, id, relative)), token);
    public Task<ScanProgress> Progress(CancellationToken token) => connection.SendAsync<ScanProgress>(McpInvocation.Tag(new Request("Progress")), token);
    public Task<FileSearchPage> Files(FileSearchQuery query, CancellationToken token) => connection.SendAsync<FileSearchPage>(McpInvocation.Tag(new Request("Files", Files: query)), token);
    public Task<AnalysisPage> Analysis(AnalysisQuery query, CancellationToken token) => connection.SendAsync<AnalysisPage>(McpInvocation.Tag(new Request("Analysis", Analysis: query)), token);
    public Task<string> DailyReport(CancellationToken token) => connection.SendAsync<string>(McpInvocation.Tag(new Request("DailyReport")), token);
    public Task<DocumentPage> Document(string category,DocumentQuery query,CancellationToken token) => connection.SendAsync<DocumentPage>(McpInvocation.Tag(new Request("ReadDocument",category,Document:query)),token);
    public Task<DocumentListPage> Documents(string category,DocumentListQuery query,CancellationToken token) => connection.SendAsync<DocumentListPage>(McpInvocation.Tag(new Request("ListDocuments",category,Documents:query)),token);
}
