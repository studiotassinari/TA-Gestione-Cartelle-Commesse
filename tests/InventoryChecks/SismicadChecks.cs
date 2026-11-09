using System.IO.Compression;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;
using TA_Gestione_Cartelle_Commesse.Services;
internal static class SismicadChecks
{
    public static void Run(string parent)
    {
        var root=Directory.CreateDirectory(Path.Combine(parent,"excluded-test")).FullName;
        var model=Directory.CreateDirectory(Path.Combine(root,"Calcolo", "Sottocartella")).Parent!.FullName;
        File.WriteAllText(Path.Combine(model,"MAIN.SCJ"),"test");
        File.WriteAllText(Path.Combine(model,"Sottocartella","relazione_geologica.pdf"),"escluso");
        File.WriteAllText(Path.Combine(root,"relazione_geologica.pdf"),"incluso");
        var names=new[]{"Progetto/MAIN.SCJ","Progetto/Sub/MUR.pdf","Progetto2/MUR.pdf","Esterni/Relazione_geologica.pdf"};
        foreach(var extension in new[]{"zip","7z"})
        {
            var path=Path.Combine(root,"modelli."+extension);
            if(extension=="zip")
            {using var zip=ZipFile.Open(path,ZipArchiveMode.Create);foreach(var name in names){using var stream=zip.CreateEntry(name).Open();stream.WriteByte(1);}}
            else
            {using var output=File.Create(path);using var writer=WriterFactory.OpenWriter(output,ArchiveType.SevenZip,new SevenZipWriterOptions(CompressionType.LZMA2));foreach(var name in names){using var input=new MemoryStream([1]);writer.Write(name,input,DateTime.UtcNow);}}
            var result=FileInventoryReader.Read(path,new HashSet<string>(),default);
            Check(result.Complete&&!result.Entries.Any(e=>e.RelativePath.StartsWith("Progetto/"))&&result.Entries.Any(e=>e.RelativePath=="Progetto2/MUR.pdf"),extension+": radice Sismicad e discendenti esclusi, fratelli conservati");
            var inspected=ArchivedJobsReader.Inspect(path,default,true);
            Check(inspected.FileCount==2,"Ispezione archivio esclude i file di progetto");
        }
        var inventory=FileInventoryReader.Read(root,new HashSet<string>(),default);
        Check(inventory.Complete&&!inventory.Entries.Any(e=>e.Container==null&&e.RelativePath.StartsWith("Calcolo/"))&&inventory.Entries.Any(e=>e.RelativePath=="relazione_geologica.pdf"),"Cartelle riconosciute da main.scj senza leggere i contenuti");
        Check(File.Exists(Path.Combine(model,"MAIN.SCJ")),"Originali invariati");
    }
    private static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS: "+text);}
}
