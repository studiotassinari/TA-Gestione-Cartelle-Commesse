using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;

internal static class GeologyOpenChecks
{
    public static async Task Run(string root)
    {
        void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS: "+text);}
        var source=Path.Combine(root,"copy-originals");var cache=Path.Combine(root,"copy-cache");Directory.CreateDirectory(source);
        var document=Path.Combine(source,"relazione.txt");const string text="Documento sintetico geologico.";File.WriteAllText(document,text);
        var stamp=File.GetLastWriteTimeUtc(document);var hash=SHA256.HashData(File.ReadAllBytes(document));
        var copy=GeologyDocumentCopy.CreateForRoots(new(document,null),[source],cache);
        Check(File.ReadAllText(copy)==text&&(File.GetAttributes(copy)&FileAttributes.ReadOnly)!=0,"Doppio clic prepara una copia locale in sola lettura");
        Check(File.ReadAllText(copy+":Zone.Identifier").Contains("ZoneId=3"),"La copia mantiene le protezioni del visualizzatore Office");
        foreach(var extension in new[]{"zip","7z"})
        {
            var file=Path.Combine(source,"relazioni."+extension);
            if(extension=="zip"){using var archive=ZipFile.Open(file,ZipArchiveMode.Create);using var writer=new StreamWriter(archive.CreateEntry("Progetto/relazione.txt").Open());writer.Write(text);}
            else{using var output=File.Create(file);using var archive=WriterFactory.OpenWriter(output,ArchiveType.SevenZip,new SevenZipWriterOptions(CompressionType.LZMA2));using var input=new MemoryStream(Encoding.UTF8.GetBytes(text));archive.Write("Progetto/relazione.txt",input,DateTime.UtcNow);}
            var before=SHA256.HashData(File.ReadAllBytes(file));var result=GeologyDocumentCopy.CreateForRoots(new(file,"Progetto/relazione.txt"),[source],cache);
            Check(File.ReadAllText(result)==text&&SHA256.HashData(File.ReadAllBytes(file)).SequenceEqual(before),"Apertura del documento dentro "+extension+" senza cambiare il contenitore");
        }
        Check(SHA256.HashData(File.ReadAllBytes(document)).SequenceEqual(hash)&&File.GetLastWriteTimeUtc(document)==stamp,"Contenuto e ultima modifica dell'originale invariati");
        try{GeologyDocumentCopy.CreateForRoots(new(Path.Combine(root,"esterno.txt"),null),[source],cache);throw new Exception("Percorso esterno accettato");}catch(UnauthorizedAccessException){}
        var malicious=Path.Combine(source,"relazione.pdf.exe");File.WriteAllText(malicious,"fixture");
        try{GeologyDocumentCopy.CreateForRoots(new(malicious,null),[source],cache);throw new Exception("Eseguibile accettato");}catch(IOException){}
        try{await GeologyDocumentCopy.PrepareAsync(new GeologicalReport{PhysicalPath=document,FileName="relazione.txt"},default);throw new Exception("Radice di test ammessa dal processo produzione");}
        catch(IOException ex){Check(ex.Message.Contains("sorgenti aziendali"),"Processo isolato di apertura risponde e rifiuta origini non autorizzate senza avviare il client");}
        foreach(var file in Directory.EnumerateFiles(cache,"*",SearchOption.AllDirectories))File.SetAttributes(file,FileAttributes.Normal);
    }
}
