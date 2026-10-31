using System.Text.Json;
using TA.Mcp;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Models;
using ModelContextProtocol.Client;

void Check(bool ok, string text) { if (!ok) throw new Exception(text); }
var source = new FakeSource();
var tools = new CommesseTools(source);
using var search = JsonDocument.Parse(await tools.Search("C", "Comune", 0, 1));
Check(search.RootElement.GetProperty("total").GetInt32() == 2, "Ricerca");
Check(search.RootElement.GetProperty("nextOffset").GetInt32() == 1, "Paginazione");
Check(search.RootElement.GetProperty("records")[0].GetProperty("bytes").ValueKind == JsonValueKind.Null, "Dimensione assente non zero");
async Task Rejected(Func<Task> run) { var calls = source.Calls; try { await run(); } catch (ArgumentException) { Check(calls == source.Calls, "Input rifiutato prima della rete"); return; } throw new Exception("Input non rifiutato"); }
await Rejected(() => tools.Search("X"));
await Rejected(() => tools.Search("C", offset: -1));
await Rejected(() => tools.Search("C", limit: 101));
await Rejected(() => tools.Children("C", source.Rows[0].Id, "../altro"));
await Rejected(() => tools.Children("C", source.Rows[0].Id, "C:\\altro"));
await Rejected(() => tools.Detail("C", "invalid"));
await Rejected(() => tools.Analysis("files"));
await Rejected(() => tools.Analysis("patterns"));
await Rejected(() => tools.Analysis("delete"));
using var first = JsonDocument.Parse(await tools.DailyReport(length: 5));
var version = first.RootElement.GetProperty("version").GetString();
using var second = JsonDocument.Parse(await tools.DailyReport(5, 5, version));
Check(second.RootElement.GetProperty("html").GetString() == source.Report.Substring(5,5), "Report paginato");
source.Report += "changed";
try { await tools.DailyReport(5, 5, version); throw new Exception("Versione report non verificata"); } catch (ArgumentException) { }

// Prova reale del protocollo stdio: nessuna connessione a dati aziendali.
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var transport = new StdioClientTransport(new StdioClientTransportOptions {
    Name = "TA MCP test", Command = "dotnet", Arguments = [Path.GetFullPath("TA.Mcp/bin/Release/net10.0-windows/TA.Mcp.dll")],
    EnvironmentVariables = new Dictionary<string,string?> { ["TA_MCP_SERVER"] = "localhost", ["TA_MCP_PORT"] = "5443" }
});
await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
var catalog = await client.ListToolsAsync(cancellationToken: timeout.Token);
Check(catalog.Select(t => t.Name).Order().SequenceEqual(new[] { "analisi_file", "cerca_cartelle", "dettaglio_cartella", "report_giornaliero", "sottocartelle" }), "Solo strumenti autorizzati");
var invalid = await client.CallToolAsync("cerca_cartelle", new Dictionary<string,object?> { ["category"] = "X" }, cancellationToken: timeout.Token);
Check(invalid.IsError == true, "Errore MCP strutturato");
Console.WriteLine("PASS: ricerca, pagine, dimensioni mancanti, validazione percorsi, versioni report, avvio stdio e catalogo in sola lettura.");

sealed class FakeSource : IReadOnlySource
{
    public Task<AnalysisPage> Analysis(AnalysisQuery query, CancellationToken token) => Task.FromResult(new AnalysisPage(null, 0, null, Array.Empty<object>(), "test"));
    public int Calls;
    public string Report = "<html>report giornaliero</html>";
    public List<FolderRecord> Rows = [new("C26001","26","001","Comune","Opera","C:/test/1","OK"), new("C26002","26","002","Comune","Opera","C:/test/2","OK")];
    public Task<CategoryState> State(string c, CancellationToken token) { Calls++; return Task.FromResult(new CategoryState(c[0], "test", false, "", "", new ArchiveSnapshot { Records = Rows })); }
    public Task<FolderEntry[]> Children(string c, string id, string relative, CancellationToken token) { Calls++; return Task.FromResult<FolderEntry[]>([]); }
    public Task<string> DailyReport(CancellationToken token) { Calls++; return Task.FromResult(Report); }
}
