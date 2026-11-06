using System.Security.Cryptography;
using System.Text.Json;
using SharpCompress.Archives;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;

namespace TA.Server.Documents;
public sealed record DocumentWork(string AllowedRoot,string FolderRoot,DocumentQuery? Read=null,DocumentListQuery? List=null);
public sealed record DocumentWorkReply(DocumentPage? Document=null,DocumentListPage? Listing=null,string? Error=null);

// Una richiesta, un processo breve. Legge un solo documento oppure i nomi di un solo livello.
public static class DocumentWorker
{
    private const int MaximumBytes=50*1024*1024;
    public static async Task RunAsync()
    {
        try
        {
            var line=await Console.In.ReadLineAsync();if(line==null||line.Length>20000)throw new InvalidDataException("Richiesta documento non valida.");
            var work=JsonSerializer.Deserialize<DocumentWork>(line)??throw new InvalidDataException("Richiesta vuota.");
            var reply=Execute(work);await Console.Out.WriteLineAsync(JsonSerializer.Serialize(reply));
        }
        catch(Exception ex){await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new DocumentWorkReply(Error:ex is System.Xml.XmlException ? "Documento Office non valido: XML malformato o DTD non ammesso." : ex.Message)));}
    }
    public static DocumentWorkReply Execute(DocumentWork work)
    {
        if((work.Read==null)==(work.List==null))throw new ArgumentException("Specificare una sola operazione.");
        if(work.Read is {} read){DocumentValidation.Validate(read);return new(Document:Read(work,read));}
        DocumentValidation.Validate(work.List!);return new(Listing:List(work,work.List!));
    }
    private static string PathFor(DocumentWork work,string relative)
    {
        if(FileInventoryReader.IsArchive(work.FolderRoot) && relative.Length!=0)throw new ArgumentException("Per la radice archivio usare relativePath vuoto e il percorso interno.");
        return SafeDocumentSource.Resolve(work.AllowedRoot,work.FolderRoot,relative);
    }
    private static string EntryName(string key)
    {var result=key.Replace('\\','/').TrimEnd('/');DocumentValidation.Relative(result);if(result.Length==0)throw new InvalidDataException("Nome interno vuoto.");return result;}
    private static byte[] Bounded(Stream stream)
    {
        // TEMP è configurato dal servizio sul disco del database (E: sulla VM).
        // Nome casuale e DeleteOnClose: anche la terminazione forzata del parser chiude e rimuove la copia.
        var temporary=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"TA-document-"+Guid.NewGuid().ToString("N")+".tmp");
        using var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,65536,FileOptions.DeleteOnClose|FileOptions.SequentialScan);
        var buffer=new byte[65536];int n;
        while((n=stream.Read(buffer))>0){if(output.Length+n>MaximumBytes)throw new InvalidDataException("Documento oltre 50 MiB: lettura interrotta.");output.Write(buffer,0,n);}
        output.Position=0;var bytes=new byte[checked((int)output.Length)];output.ReadExactly(bytes);return bytes;
    }
    private static DocumentPage Read(DocumentWork work,DocumentQuery query)
    {
        var path=PathFor(work,query.RelativePath);byte[] bytes;string name;
        using(var file=SafeDocumentSource.Open(work.AllowedRoot,path))
        {
            if(query.ArchiveEntry==null)
            {if(file.Length>MaximumBytes)throw new InvalidDataException("Documento oltre 50 MiB.");bytes=Bounded(file);name=System.IO.Path.GetFileName(path);}
            else
            {
                if(!FileInventoryReader.IsArchive(path))throw new ArgumentException("Il contenitore deve essere ZIP o 7z.");
                using var archive=ArchiveFactory.OpenArchive(file);var wanted=EntryName(query.ArchiveEntry);
                var matches=new List<IArchiveEntry>();int visited=0;
                foreach(var candidate in archive.Entries)
                {
                    if(++visited>200000)throw new IOException("Archivio oltre 200000 voci: lettura non eseguita.");
                    if(!candidate.IsDirectory && EntryName(candidate.Key??"")==wanted)matches.Add(candidate);
                    if(matches.Count>1)throw new IOException("Nome documento duplicato nell'archivio.");
                }
                if(matches.Count!=1)throw new IOException("Documento interno assente o ambiguo nell'archivio.");
                var entry=matches[0];if(entry.IsEncrypted)throw new IOException("Documento cifrato: non vengono richieste password.");
                if(entry.Size>MaximumBytes)throw new IOException("Documento interno oltre 50 MiB.");
                using var input=entry.OpenEntryStream();bytes=Bounded(input);name=wanted.Split('/').Last();
            }
        }
        var version=Convert.ToHexString(SHA256.HashData(bytes));
        if(query.Version!=null && query.Version!=version)throw new IOException("Documento cambiato: ripartire da offset 0 senza version.");
        DocumentText parsed;
        try{parsed=DocumentText.Read(bytes,name);}
        catch(Exception ex) when(ex is not InvalidDataException)
        {throw new InvalidDataException("Documento non interpretabile o protetto ("+ex.GetType().Name+"). Nessun contenuto riportato nella diagnostica.");}
        var text=parsed.Text;
        if(query.Offset>text.Length)throw new ArgumentException("Offset oltre il testo estratto.");
        var count=Math.Min(query.Length,text.Length-query.Offset);
        return new(query.FolderId,name,query.RelativePath,query.ArchiveEntry,version,DateTime.UtcNow,text.Length,query.Offset,
            query.Offset+count<text.Length?query.Offset+count:null,text.Substring(query.Offset,count),parsed.Truncated,parsed.Warnings);
    }
    private static DocumentListPage List(DocumentWork work,DocumentListQuery query)
    {
        var path=PathFor(work,query.RelativePath);var entries=new Dictionary<string,DocumentEntry>(StringComparer.Ordinal);
        if(FileInventoryReader.IsArchive(path) && !Directory.Exists(path))
        {
            using var file=SafeDocumentSource.Open(work.AllowedRoot,path);using var archive=ArchiveFactory.OpenArchive(file);
            var parent=(query.ArchiveDirectory??"").Replace('\\','/').TrimEnd('/');var prefix=parent.Length==0?"":parent+"/";int visited=0;
            foreach(var entry in archive.Entries)
            {
                if(++visited>200000)throw new IOException("Archivio oltre 200000 voci: elenco non restituito parzialmente.");
                var key=EntryName(entry.Key??"");if(!key.StartsWith(prefix,StringComparison.Ordinal))continue;
                var remainder=key[prefix.Length..];if(remainder.Length==0)continue;
                var slash=remainder.IndexOf('/');bool directory=slash>=0||entry.IsDirectory;
                var name=slash>=0?remainder[..slash]:remainder;var internalPath=prefix+name;
                if(entries.TryGetValue(internalPath,out var existing))
                {if(!directory || !existing.IsDirectory)throw new IOException("Nomi duplicati o ambigui nell'archivio.");continue;}
                entries.Add(internalPath,new(name,query.RelativePath,internalPath,directory,!directory&&FileInventoryReader.IsArchive(name),directory?null:entry.Size));
            }
        }
        else
        {
            if(query.ArchiveDirectory!=null)throw new ArgumentException("Percorso interno specificato senza archivio.");
            using var directory=SafeDocumentSource.OpenDirectory(work.AllowedRoot,path);int visited=0;
            foreach(var entry in Directory.EnumerateFileSystemEntries(path))
            {
                if(++visited>100000)throw new IOException("Directory oltre 100000 elementi: restringere il percorso.");
                var attributes=File.GetAttributes(entry);if((attributes&FileAttributes.ReparsePoint)!=0)continue;
                bool isDirectory=(attributes&FileAttributes.Directory)!=0;var name=System.IO.Path.GetFileName(entry);
                var relative=System.IO.Path.GetRelativePath(work.FolderRoot,entry).Replace('\\','/');DocumentValidation.Relative(relative);
                entries.Add(relative,new(name,relative,null,isDirectory,!isDirectory&&FileInventoryReader.IsArchive(entry),isDirectory?null:new FileInfo(entry).Length));
            }
        }
        var extension=query.Extension.Length==0?"":"."+query.Extension.TrimStart('.');
        var selected=entries.Values.Where(e=>e.Name.Contains(query.Name,StringComparison.OrdinalIgnoreCase) && (extension.Length==0 || !e.IsDirectory&&System.IO.Path.GetExtension(e.Name).Equals(extension,StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(e=>e.IsDirectory).ThenBy(e=>e.Name,StringComparer.Ordinal).ToArray();
        return new(query.FolderId,query.RelativePath,query.ArchiveDirectory,selected.Length,query.Offset,query.Offset+query.Limit<selected.Length?query.Offset+query.Limit:null,
            selected.Skip(query.Offset).Take(query.Limit).ToArray(),"Elenco su richiesta di un solo livello; nessuna navigazione automatica. Nomi e dimensioni attuali, senza contenuto. Archivi annidati elencati ma non espansi.");
    }
}
