using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;
using TA.Server;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;

static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
var root=Path.Combine(Path.GetTempPath(),"TA-FileSearch-"+Guid.NewGuid().ToString("N"));
var source=Directory.CreateDirectory(Path.Combine(root,"2026","C26198 - Cliente - Progetto")).FullName;
var nested=Directory.CreateDirectory(Path.Combine(source,"Progetto","Termica")).FullName;
File.WriteAllText(Path.Combine(nested,"Edilclima.E00"),"CONTENUTO NON DA LEGGERE");
File.WriteAllText(Path.Combine(source,"Edilclima.pdf"),"test");
foreach(var extension in new[]{"zip","7z"}){
    var file=Path.Combine(source,"Consegna."+extension);
    if(extension=="zip") {using var zip=ZipFile.Open(file,ZipArchiveMode.Create);using var output=new StreamWriter(zip.CreateEntry("Termica/Edilclima.E00").Open());output.Write("test");}
    else {using var output=File.Create(file);using var writer=WriterFactory.OpenWriter(output,ArchiveType.SevenZip,new SevenZipWriterOptions(CompressionType.LZMA2));using var input=new MemoryStream([1]);writer.Write("Termica/Edilclima.E00",input,DateTime.UtcNow);}
}
var store=new InventoryStore(Path.Combine(root,"json"));
var unit=new InventoryUnit(InventoryPlanner.Id(source),"C","2026","C26198","Cliente","Progetto",source,"censita");
var manifest=store.Save(unit,FileInventoryReader.Read(source,new HashSet<string>(),default),default);
var zipPath=Path.Combine(source,"Consegna.zip");
var archive=store.Save(unit with {Id=InventoryPlanner.Id(zipPath),Category="A",Path=zipPath},FileInventoryReader.Read(zipPath,new HashSet<string>(),default),default);
var gare=Directory.CreateDirectory(Path.Combine(root,"G26100 - Gara")).FullName;
File.WriteAllText(Path.Combine(gare,"Edilclima.E00"),"test");
var bid=store.Save(unit with{Id=InventoryPlanner.Id(gare),Category="G",Code="G26100",Path=gare},FileInventoryReader.Read(gare,new HashSet<string>(),default),default);
var date=DateTime.UtcNow;store.SaveRun(new(date.AddMinutes(-1),date,[manifest,archive,bid],[],[]));
var search=new FileSearchService(new(){ArchiveFolder=Path.Combine(root,"json")});
// Le ricerche devono funzionare anche senza più accesso alle sorgenti.
Directory.Move(source,source+"-spostata");Directory.Move(gare,gare+"-spostata");
var first=search.Search(new(Name:"edilclima",Extension:"E00",Category:"C",Year:"2026",Limit:1));
Check(first.Total==3 && first.NextOffset==1 && first.Files.Count==1,"filtro nome+estensione, sottocartelle, ZIP e 7z con totale esatto");
Check(first.LastScanUtc==date && first.Files[0].Code=="C26198","data scansione e codice commessa restituiti");
var second=search.Search(new(Name:"edilclima",Extension:".e00",Category:"C",Year:"2026",Offset:1,Limit:2,Version:first.Version));
Check(second.Total==3 && second.NextOffset==null && !second.Files.Any(f=>f.Location.Id==first.Files[0].Location.Id),"pagine senza ripetizioni");
var all=search.Search(new(Name:"Edilclima",Extension:"e00"));
Check(all.Total==4,"ricerca globale include gare e deduplica archivi sovrapposti");
var inner=all.Files.Single(f=>f.Location.ArchivePath?.EndsWith("Consegna.zip")==true);
Check(inner.Location.InternalPath=="Termica/Edilclima.E00" && inner.Location.PhysicalPath==zipPath && inner.RelativePath=="Termica/Edilclima.E00","indirizzo fisico archivio e percorso interno separati");
Check(all.Files.Select(f=>f.Location.Id).Distinct().Count()==4,"nomi uguali in percorsi diversi mantengono identità distinte");
Check(store.Entries(manifest).All(e=>e.Location!=null),"organizzazione e identificazione salvate nei JSON");
Check(search.Search(new(Category:"G",Code:"G26100")).Total==1,"filtro gara");
Check(search.Search(new(Category:"A",Extension:"E00")).Total==1,"filtro archivio");
Check(search.Search(new(Extension:"inesistente")).Total==0,"nessuna corrispondenza è un risultato vuoto valido");
store.SaveRun(new(date,date.AddSeconds(1),[manifest],[],[]));
try {search.Search(new(Offset:1,Version:first.Version));throw new Exception("Versione non verificata");}catch(ArgumentException){}
store.SaveRun(new(date,date,[manifest with{Complete=false}],[],[]));
Check(!search.Search(new()).Complete,"inventario parziale dichiarato");
try {new FileSearchService(new(){ArchiveFolder=Path.Combine(root,"assente")}).Search(new());throw new Exception("Inventario assente trattato come vuoto");}catch(IOException){}
Console.WriteLine("Tutti i controlli ricerca file superati.");
