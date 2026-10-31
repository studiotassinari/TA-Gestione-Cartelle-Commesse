using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;
using TA.Server;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
using TA_Gestione_Cartelle_Commesse.Models;

static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
var root = Path.Combine(Path.GetTempPath(), "TA-InventoryChecks-" + Guid.NewGuid().ToString("N"));
var jobs = Directory.CreateDirectory(Path.Combine(root,"lavori")).FullName;
var bids = Directory.CreateDirectory(Path.Combine(root,"gare")).FullName;
var archives = Directory.CreateDirectory(Path.Combine(root,"archivio")).FullName;
var job = Directory.CreateDirectory(Path.Combine(jobs,"2026","C26198 - MARINI - Progetto")).FullName;
Directory.CreateDirectory(Path.Combine(jobs,"2026","C26199 - MARINI - Altro"));
var nested = Directory.CreateDirectory(Path.Combine(job,"C26999 - Interna - Non commessa")).FullName;
File.WriteAllText(Path.Combine(nested,"Documento.pdf"),"CONTENUTO SEGRETO DA NON INDICIZZARE");
File.WriteAllText(Path.Combine(jobs,"sciolto.txt"),"test");
var candidate = Directory.CreateDirectory(Path.Combine(jobs,"MAR-marini","Impianto storico")).FullName;
File.WriteAllText(Path.Combine(candidate,"Tavola.dwg"),"test");
foreach (var extension in new[] { "zip", "7z" })
{
    var file = Path.Combine(job,"Consegna."+extension);
    if(extension == "zip")
    {
        using var zip = ZipFile.Open(file,ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry("Disegni/Sezione.dwg").Open()); writer.Write("test");
    }
    else
    {
        using var output = File.Create(file);
        using var writer = WriterFactory.OpenWriter(output,ArchiveType.SevenZip,new SevenZipWriterOptions(CompressionType.LZMA2));
        using var input = new MemoryStream([1,2,3]); writer.Write("Disegni/Sezione.dwg",input,DateTime.UtcNow);
    }
}
var before = Directory.GetFiles(jobs,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))),File.GetLastWriteTimeUtc(p)));
var settings = new ServerSettings { JobsRoot=jobs,BidsRoot=bids,ArchivedJobsRoot=archives,ArchiveFolder=Path.Combine(root,"JSON") };
var service = new AnalysisService(settings,NullLogger<AnalysisService>.Instance);
CategoryState[] States() => new[] { ('C',jobs),('G',bids),('A',archives) }.Select(p=>new CategoryState(p.Item1,p.Item2,false,"","",new ArchiveSnapshot { Records = p.Item1 == 'A' ? [] : FolderReader.Scan(p.Item2,p.Item1,default).Records })).ToArray();
await service.RunAsync(States(),default);
var store = new InventoryStore(settings.ArchiveFolder);
var run = store.Latest()!;
var manifest = run.Inventories.Single(m=>m.Unit.Path==job);
var entries = store.Entries(manifest).ToArray();
Check(entries.Any(e=>e.RelativePath.EndsWith("Documento.pdf")),"file annidati censiti senza promuovere commesse interne");
Check(!run.Inventories.Any(m=>m.Unit.Path==nested),"commessa annidata non classificata");
Check(entries.Count(e=>e.RelativePath=="Disegni/Sezione.dwg" && e.Container!=null)==2,"ZIP e 7z: nomi interni e directory implicite");
Check(run.Candidates.Any(c=>c.Path==candidate && c.Client=="MARINI" && c.Confidence=="media"),"committente ricorrente e progetto come proposta");
Check(run.Inventories.SelectMany(store.Entries).Any(e=>e.RelativePath=="sciolto.txt"),"file non assegnati coperti");
Check(store.UnitDirectory(manifest.Unit).Contains(Path.Combine("C","2026","C26198")),"JSON separati per categoria, anno e codice");
Check(before.All(p=>File.GetLastWriteTimeUtc(p.Key)==p.Value.Item2 && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p.Key)))==p.Value.Item1) && Directory.GetFiles(jobs,"*",SearchOption.AllDirectories).Length==before.Count,"sorgenti invariate: hash, date e nessun file estratto");
Check(!Directory.EnumerateFiles(settings.ArchiveFolder,"*.json",SearchOption.AllDirectories).Any(p=>File.ReadAllText(p).Contains("CONTENUTO SEGRETO")),"nessun contenuto documentale nei JSON");
Check(manifest.Baseline && manifest.Added==0,"prima lettura non genera false differenze");
File.WriteAllText(Path.Combine(job,"Nuovo.txt"),"123");
await service.RunAsync(States(),default);
manifest=store.Latest()!.Inventories.Single(m=>m.Unit.Path==job);
Check(manifest.Added==1 && manifest.Removed==0,"seconda scansione segnala nuovo nome nel report");
File.Delete(Path.Combine(job,"Nuovo.txt"));
await service.RunAsync(States(),default);
manifest=store.Latest()!.Inventories.Single(m=>m.Unit.Path==job);
Check(manifest.Removed==1,"rimozione confermata dopo lettura completa");
var partial=store.Save(manifest.Unit,new([], ["Rete non disponibile"],false),default);
Check(partial.Removed==0 && partial.EntryCount==manifest.EntryCount && !partial.Complete,"lettura incompleta conserva nomi e non inventa rimozioni");
var paged=store.Save(manifest.Unit,new(Enumerable.Range(0,2101).Select(n=>new InventoryEntry($"File-{n:D5}.pdf","file")).ToList(),[],true),default);
Check(paged.PageCount==3 && store.Entries(paged).Count()==2101,"JSON suddivisi in pagine complete");
store.SaveRun(new(DateTime.UtcNow,DateTime.UtcNow,[paged],[],[]));
var page=service.Query(new("files",manifest.Unit.Id,"",1000,100));
Check(page.Total==2101 && page.NextOffset==1100 && ((List<object>)page.Items).Count==100,"paginazione MCP su più file JSON");
var latest=File.ReadAllText(Path.Combine(store.UnitDirectory(paged.Unit),"latest.json"));
using var cancelled=new CancellationTokenSource();cancelled.Cancel();
try { store.Save(paged.Unit,new([new("no.txt","file")],[],true),cancelled.Token); throw new Exception("Annullamento ignorato"); } catch(OperationCanceledException){}
Check(File.ReadAllText(Path.Combine(store.UnitDirectory(paged.Unit),"latest.json"))==latest,"annullamento preserva puntatore a dati completi");
try { store.UnitDirectory(paged.Unit with { Id="../../altro" }); throw new Exception("Traversal ammesso"); } catch(InvalidDataException){}
Check(service.Report().Contains("Nessuna notifica"),"report sintetico disponibile dal server");
var quiet=new ChangeHub(false);using var subscription=quiet.Subscribe();await subscription.ReadAsync(default);quiet.Publish("non deve arrivare");
using var timeout=new CancellationTokenSource(150);
try { await subscription.ReadAsync(timeout.Token);throw new Exception("Notifica inviata"); } catch(OperationCanceledException){}
Check(true,"nessuna notifica differenze");
Console.WriteLine("Fixtures conservate per verifica: "+root);
