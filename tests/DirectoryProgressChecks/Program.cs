using System.IO.Compression;
using TA_Gestione_Cartelle_Commesse.Services;
void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS: "+text);}
var root=Path.Combine(Path.GetTempPath(),"TA-DirectoryProgress-"+Guid.NewGuid().ToString("N"));
try
{
    var job=Directory.CreateDirectory(Path.Combine(root,"2026","C26198 - Cliente - Progetto")).FullName;
    var nested=Directory.CreateDirectory(Path.Combine(job,"Impianti","Termico")).FullName;
    var file=Path.Combine(nested,"calcolo.ec");File.WriteAllText(file,"fixture");
    var zip=Path.Combine(job,"C26198 - Cliente - Archivio.zip");
    using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create)){using var writer=new StreamWriter(archive.CreateEntry("Disegni/Strutture/tavola.dwg").Open());writer.Write("fixture");}
    var paths=new List<string>();
    FolderInspectionService.Inspect(job,CancellationToken.None,paths.Add);
    Check(paths.Contains(nested),"Lettura metadati comunica la sottocartella reale");
    paths.Clear();FileInventoryReader.Read(job,new HashSet<string>(),CancellationToken.None,paths.Add);
    Check(paths.Contains(nested) && paths.Contains(zip+" > Disegni/Strutture"),"Inventario comunica cartelle e percorsi interni ZIP senza estrazione");
    paths.Clear();var scan=ArchivedJobsReader.ScanRoots([root],CancellationToken.None,paths.Add);
    // Il censimento dei contenitori si ferma al primo livello; l'inventario interno resta approfondito.
    Check(paths.Contains(root) && paths.Contains(Path.Combine(root,"2026")),"Censimento archivi comunica radice e sottocartella immediata");
    Check(!paths.Contains(job) && !paths.Contains(nested) && scan.Records.Count==0,"Censimento archivi non visita i livelli esclusi e non include i loro ZIP");
    paths.Clear();ArchivedJobsReader.Inspect(zip,CancellationToken.None,true,paths.Add);
    Check(paths.Contains(zip+" > Disegni/Strutture"),"Verifica metadati ZIP comunica la directory interna");
    Check(File.ReadAllText(file)=="fixture" && Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length==2,"Sorgenti invariate e nessun file estratto");
}
finally
{
    if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("TA-DirectoryProgress-"))Directory.Delete(root,true);
}
