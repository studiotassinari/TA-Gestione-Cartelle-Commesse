using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;

namespace TA.Mcp;

public sealed class LocalSource : IReadOnlySource
{
    private readonly LocalPipeConnection connection = new();
    public Task<CategoryState> State(string category, CancellationToken token) => connection.SendAsync<CategoryState>(new Request("State", category), token);
    public Task<FolderEntry[]> Children(string category, string id, string relative, CancellationToken token) => connection.SendAsync<FolderEntry[]>(new Request("Children", category, id, relative), token);
    public Task<AnalysisPage> Analysis(AnalysisQuery query, CancellationToken token) => connection.SendAsync<AnalysisPage>(new Request("Analysis", Analysis: query), token);
    public Task<string> DailyReport(CancellationToken token) => connection.SendAsync<string>(new Request("DailyReport"), token);
}
