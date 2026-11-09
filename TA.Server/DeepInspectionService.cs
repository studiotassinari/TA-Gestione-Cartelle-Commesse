using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Models;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA.Server;

// Un approfondimento esplicito alla volta. Nessuna chiamata cloud o lettura automatica dei contenuti.
public sealed class DeepInspectionService(ServerSettings settings,ScanCoordinator scans):IDisposable
{
    private sealed class Job(string category,FolderRecord record,string terms)
    {
        public readonly string Id=Guid.NewGuid().ToString("N");
        public readonly string Category=category,Terms=terms;
        public readonly FolderRecord Record=record;
        public readonly CancellationTokenSource Cancel=new(TimeSpan.FromMinutes(3));
        public string State="In corso",Location=record.Path,Error="";
        public DateTime Started=DateTime.UtcNow;
        public DateTime? Finished;
        public JsonElement? Result;
    }
    private readonly object gate=new();
    private readonly List<Job> jobs=[];
    public JsonElement Execute(string category,DeepInspectionQuery q)
    {
        if(category is not ("C" or "G" or "A")||q.Action is not ("avvia" or "stato" or "annulla")||q.Terms==null||q.Terms.Length>300||q.FolderId==null||q.FolderId.Length>128||q.JobId==null||q.JobId.Length>64||q.Offset<0||q.Offset>1000||q.Limit is <1 or >100)throw new ArgumentException("Richiesta di approfondimento non valida.");
        lock(gate)
        {
            Job job;
            if(q.Action=="avvia")
            {
                if(jobs.Any(j=>j.Finished==null))throw new IOException("Un approfondimento è già in corso: consultarne lo stato o annullarlo.");
                var record=scans.Get(category).State.Snapshot?.Records.SingleOrDefault(r=>r.Id==q.FolderId)??throw new ArgumentException("Usare l'ID di una commessa restituita da cerca_cartelle.");
                ValidateRoot(settings,category,record.Path);
                while(jobs.Count>=8){jobs[0].Cancel.Dispose();jobs.RemoveAt(0);}
                job=new(category,record,q.Terms);jobs.Add(job);
                _=Task.Run(()=>Run(job));
            }
            else
            {
                job=jobs.SingleOrDefault(j=>j.Id==q.JobId&&j.Category==category)??throw new ArgumentException("Approfondimento non disponibile: ID errato o servizio riavviato.");
                if(q.Action=="annulla"&&job.Finished==null){job.Cancel.Cancel();job.State="Annullamento richiesto";}
            }
            var result=job.Result;
            var all=result?.GetProperty("Documents").EnumerateArray().ToArray()??[];
            return JsonSerializer.SerializeToElement(new{job.Id,job.Category,FolderId=job.Record.Id,job.Record.Code,job.State,job.Started,job.Finished,job.Location,job.Error,
                CustomerFromFolderName=job.Record.Client,CustomerVerified=false,Total=all.Length,Offset=q.Offset,NextOffset=q.Offset+q.Limit<all.Length?(int?)(q.Offset+q.Limit):null,
                Documents=all.Skip(q.Offset).Take(q.Limit),Summary=result?.GetProperty("Summary"),
                Note="Risultati da nomi e metadati, non conferme dal contenuto. Usare leggi_documento sui percorsi restituiti per verificare committente o documenti specifici. Nessuna approvazione di preventivi è deducibile dal nome. Originali in sola lettura. Lavoro esplicito limitato a tre minuti; nessun rinnovo automatico."});
        }
    }
    private void Run(Job job)
    {
        try
        {
            var result=Inspect(settings,job.Category,job.Record,job.Terms,job.Cancel.Token,p=>{lock(gate)job.Location=p;});
            job.Cancel.Token.ThrowIfCancellationRequested();
            new InventoryStore(settings.InventoryFolder).SaveDocument("approfondimento:"+job.Category+":"+job.Record.Id,result);
            lock(gate){job.Result=result;job.State="Completato";}
        }
        catch(OperationCanceledException){lock(gate){job.State="Interrotto";job.Error="Annullato o raggiunto il limite di tre minuti; nessun risultato incompleto sostituisce quello salvato.";}}
        catch(Exception ex){lock(gate){job.State="Errore";job.Error=ex.Message;}}
        finally{lock(gate)job.Finished=DateTime.UtcNow;}
    }
    public static void ValidateRoot(ServerSettings settings,string category,string path)
    {
        var root=category switch{"C"=>settings.JobsRoot,"G"=>settings.BidsRoot,"A"=>ArchiveScope.Root(settings.ArchivedJobsRoot),_=>throw new ArgumentException("Categoria non valida")};
        var relative=Path.GetRelativePath(root,path);
        if(Path.IsPathRooted(relative)||relative=="."||relative.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).Any(p=>p=="..")||category=="A"&&!ArchiveScope.Contains(root,path))throw new UnauthorizedAccessException("Cartella fuori dall'ambito consentito.");
    }
    public static JsonElement Inspect(ServerSettings settings,string category,FolderRecord record,string terms,CancellationToken token,Action<string>? progress=null)
    {
        ValidateRoot(settings,category,record.Path);
        var words=(string.IsNullOrWhiteSpace(terms)?"preventivo;offerta;contratto;incarico;geolog;geotec;MUR;IPRIPI;elenco":terms).Split([';',','],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        var inventory=FileInventoryReader.Read(record.Path,new HashSet<string>(StringComparer.OrdinalIgnoreCase),token,progress,maximum:100000);
        var candidates=inventory.Entries.Where(e=>e.Kind!="directory"&&words.Any(w=>e.RelativePath.Contains(w,StringComparison.OrdinalIgnoreCase))).ToArray();
        var rows=candidates.Take(1000).Select(e=>new{e.RelativePath,e.Bytes,e.ModifiedUtc,Container=e.Container,
            ReadRelativePath=category=="A"?"":e.Container??e.RelativePath,ArchiveEntry=e.Container==null?null:e.RelativePath,
            Match="Candidato dal nome, contenuto da verificare"});
        return JsonSerializer.SerializeToElement(new{Documents=rows,Summary=new{record.Id,record.Code,CheckedUtc=DateTime.UtcNow,inventory.Complete,FilesExamined=inventory.Entries.Count(e=>e.Kind!="directory"),Matches=candidates.Length,Truncated=candidates.Length>1000,inventory.Issues,Criteria=words}});
    }
    public void Dispose(){lock(gate)foreach(var job in jobs)if(job.Finished==null)job.Cancel.Cancel();}
}
