using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using TA.Mcp;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Models;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS: "+text);}
string Session(bool contents=false)=>JsonSerializer.SerializeToElement(ConsultationSessions.Shared.Open(contents)).GetProperty("consultazione").GetString()!;
void Denied(Action action){try{action();}catch(InvalidOperationException){return;}throw new Exception("Richiesta non rifiutata");}
try { await McpErrors.Run<int>(()=>Task.FromException<int>(new TA.Transport.ServerRejectedException("Inventario parziale: usare elenca_documenti"))); throw new Exception("Errore non comunicato"); }
catch(ModelContextProtocol.McpException ex) { Check(ex.Message.Contains("Inventario parziale"),"Errore del servizio comunicato al plugin senza nasconderlo come errore generico"); }
var source=new FakeSource();var tools=new CommesseTools(source);
using var search=JsonDocument.Parse(await tools.Search("C","Comune",0,1,Session()));
Check(search.RootElement.GetProperty("total").GetInt32()==2 && search.RootElement.GetProperty("nextOffset").GetInt32()==1,"Ricerca e paginazione");
Check(search.RootElement.GetProperty("records")[0].GetProperty("bytes").ValueKind==JsonValueKind.Null,"Dimensione assente non zero");
async Task Rejected(Func<Task> run){var calls=source.Calls;try{await run();}catch(ArgumentException){Check(calls==source.Calls,"Input rifiutato prima della rete");return;}throw new Exception("Input non rifiutato");}
await Rejected(()=>tools.Search("X",consultazione:Session()));
await Rejected(()=>tools.Search("C",offset:-1,consultazione:Session()));
await Rejected(()=>tools.Search("C",limit:101,consultazione:Session()));
await Rejected(()=>tools.Children("C",source.Rows[0].Id,"../altro",consultazione:Session()));
await Rejected(()=>tools.Children("C",source.Rows[0].Id,"C:\\altro",consultazione:Session()));
await Rejected(()=>tools.Detail("C","invalid",consultazione:Session()));
await Rejected(()=>tools.Analysis("files",consultazione:Session()));
await Rejected(()=>tools.Analysis("patterns",consultazione:Session()));
await Rejected(()=>tools.Analysis("delete",consultazione:Session()));
await Rejected(()=>tools.Files(extension:"../pdf",consultazione:Session()));
await Rejected(()=>tools.Files(category:"X",consultazione:Session()));
await Rejected(()=>tools.Files(offset:1,consultazione:Session()));
await Rejected(()=>tools.Files(year:"26",consultazione:Session()));
using var first=JsonDocument.Parse(await tools.DailyReport(length:5,consultazione:Session()));
var version=first.RootElement.GetProperty("version").GetString();
using var second=JsonDocument.Parse(await tools.DailyReport(5,5,version,Session()));
Check(second.RootElement.GetProperty("html").GetString()==source.Report.Substring(5,5),"Report paginato");
source.Report+="changed";try{await tools.DailyReport(5,5,version,Session());throw new Exception("Versione ignorata");}catch(ArgumentException){Check(true,"Report cambiato rifiutato");}
await Rejected(()=>tools.Document("C",source.Rows[0].Id,"../altro.txt",consultazione:Session(true)));

// Le sessioni sono limitate anche senza timer; l'apertura non interroga la sorgente.
var now=DateTimeOffset.UtcNow;var sessions=new ConsultationSessions(()=>now);
string Open(bool contents=false)=>JsonSerializer.SerializeToElement(sessions.Open(contents)).GetProperty("consultazione").GetString()!;
var onlyNames=Open();Denied(()=>sessions.Use(onlyNames,true));sessions.Use(onlyNames,false);
var content=Open(true);sessions.Use(content,true);now=now.AddMinutes(2);Denied(()=>sessions.Use(content,true));
var bounded=Open();for(int i=0;i<20;i++)sessions.Use(bounded,false);Denied(()=>sessions.Use(bounded,false));
Denied(()=>sessions.Use("",false));Check(true,"Scadenza, massimo operazioni, assenza di rinnovo e permesso contenuti");
Denied(()=>new LocalSource().Progress(default));
Denied(()=>new WindowsSource("localhost",1).Progress(default));
Check(true,"Nessun accesso alla sorgente fuori da una chiamata strumento attiva");
var invocation=McpInvocation.Begin("cerca_cartelle",Session());
Check(TA.Mcp.McpInvocation.Tag(new TA.Transport.Request("State")).McpTool=="cerca_cartelle","Richiesta tracciata con lo strumento");
invocation.Dispose();Denied(()=>McpInvocation.Tag(new TA.Transport.Request("State")));

// Server TCP fittizio: conta i tentativi, senza leggere dati o usare servizi aziendali.
using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int connections=0;
using var stopping=new CancellationTokenSource();
var accepting=Task.Run(async()=>{try{while(true){using var peer=await listener.AcceptTcpClientAsync(stopping.Token);Interlocked.Increment(ref connections);}}catch(OperationCanceledException){} });
using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
var transport=new StdioClientTransport(new StdioClientTransportOptions{
    Name="TA MCP test",Command="dotnet",Arguments=[Path.GetFullPath("TA.Mcp/bin/Release/net10.0-windows/TA.Mcp.dll")],
    EnvironmentVariables=new Dictionary<string,string?>{["TA_MCP_SERVER"]="127.0.0.1",["TA_MCP_PORT"]=((IPEndPoint)listener.LocalEndpoint).Port.ToString(),["TA_MCP_TRANSPORT"]="kerberos"}
});
await using var client=await McpClient.CreateAsync(transport,cancellationToken:timeout.Token);
var catalog=await client.ListToolsAsync(cancellationToken:timeout.Token);
Check(catalog.Select(t=>t.Name).Order().SequenceEqual(new[]{"prepara_zip_cartella","stato_zip_cartella","elimina_cartella_zippata","approfondisci_commessa","analisi_file","cerca_cartelle","cerca_file","cerca_relazioni_geologiche","dettaglio_cartella","elenca_documenti","inizia_consultazione","leggi_documento","leggi_relazione_geologica","report_giornaliero","sottocartelle","stato_analisi","stato_scansione","prossime_relazioni_da_analizzare","salva_analisi_geologica","elenca_fascicoli_geologici","cerca_mur_ipripi","regole_ipripi","leggi_mur_ipripi","salva_analisi_ipripi"}.Order()),"Ventiquattro strumenti: consultazione, catalogazione e archiviazione esplicita");
await Task.Delay(300);
Check(connections==0,"Avvio, catalogo e inattività senza connessioni al servizio");
var noSession=await client.CallToolAsync("cerca_cartelle",new Dictionary<string,object?>{["category"]="C"},cancellationToken:timeout.Token);
Check(noSession.IsError==true && connections==0,"Vecchie chiamate automatiche rifiutate prima della rete");
var opening=await client.CallToolAsync("inizia_consultazione",new Dictionary<string,object?>(),cancellationToken:timeout.Token);
using var openingJson=JsonDocument.Parse(((TextContentBlock)opening.Content[0]).Text);
var session=openingJson.RootElement.GetProperty("consultazione").GetString();
Check(connections==0,"Apertura consultazione senza lettura dati");
var invalid=await client.CallToolAsync("cerca_cartelle",new Dictionary<string,object?>{["category"]="X",["consultazione"]=session},cancellationToken:timeout.Token);
Check(invalid.IsError==true && connections==0,"Errore MCP strutturato senza connessione");
var refusedContent=await client.CallToolAsync("leggi_documento",new Dictionary<string,object?>{["category"]="C",["id"]=source.Rows[0].Id,["relativePath"]="prova.txt",["consultazione"]=session},cancellationToken:timeout.Token);
Check(refusedContent.IsError==true && connections==0,"Sessione metadati non abilita lettura contenuti");
var deniedGeology=await client.CallToolAsync("leggi_relazione_geologica",new Dictionary<string,object?>{["id"]="geo-1",["consultazione"]=session},cancellationToken:timeout.Token);
Check(deniedGeology.IsError==true && connections==0,"Relazione geologica richiede consultazione contenuti prima di accedere al servizio");
var valid=await client.CallToolAsync("cerca_cartelle",new Dictionary<string,object?>{["category"]="C",["consultazione"]=session},cancellationToken:timeout.Token);
Check(valid.IsError==true && connections==1,"Una richiesta esplicita produce un solo collegamento, nessun ritentativo automatico");
await Task.Delay(500);Check(connections==1,"Nessuna attività residua dopo la risposta");
stopping.Cancel();await accepting;

sealed class FakeSource:IReadOnlySource
{
    public Task<AnalysisPage> Analysis(AnalysisQuery q,CancellationToken t)=>Task.FromResult(new AnalysisPage(null,0,null,Array.Empty<object>(),"test"));
    public Task<ScanProgress> Progress(CancellationToken t)=>Task.FromResult(new ScanProgress(true,"Inventario nomi",1,4,"test"));
    public Task<FileSearchPage> Files(FileSearchQuery q,CancellationToken t)=>Task.FromResult(new FileSearchPage("v",DateTime.UtcNow,DateTime.UtcNow,0,q.Offset,null,true,[],[]));
    public int Calls;public string Report="<html>report giornaliero</html>";
    public List<FolderRecord> Rows=[new("C26001","26","001","Comune","Opera","C:/test/1","OK"),new("C26002","26","002","Comune","Opera","C:/test/2","OK")];
    public Task<CategoryState> State(string c,CancellationToken t){Calls++;return Task.FromResult(new CategoryState(c[0],"test",false,"","",new ArchiveSnapshot{Records=Rows}));}
    public Task<FolderEntry[]> Children(string c,string id,string path,CancellationToken t){Calls++;return Task.FromResult<FolderEntry[]>([]);}
    public Task<string> DailyReport(CancellationToken t){Calls++;return Task.FromResult(Report);}
}
