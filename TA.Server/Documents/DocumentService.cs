using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Contracts;

namespace TA.Server.Documents;
public sealed class DocumentService(ServerSettings settings,ScanCoordinator scans)
{
    private readonly SemaphoreSlim slots=new(2);
    public async Task<DocumentWorkReply> ExecuteAsync(string category,DocumentQuery? read,DocumentListQuery? list,CancellationToken token)
    {
        if(read!=null)DocumentValidation.Validate(read);else if(list!=null)DocumentValidation.Validate(list);else throw new ArgumentException("Richiesta mancante.");
        var id=read?.FolderId??list!.FolderId;
        var state=scans.Get(category).State;
        var record=state.Snapshot?.Records.SingleOrDefault(r=>r.Id==id)??throw new KeyNotFoundException("Cartella non presente nel censimento: usare cerca_cartelle.");
        var roots=new[]{settings.JobsRoot,settings.BidsRoot,settings.ArchivedJobsRoot};
        var full=Path.GetFullPath(record.Path);
        var allowed=roots.FirstOrDefault(r=>full.StartsWith(Path.GetFullPath(r).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))??throw new UnauthorizedAccessException("Sorgente esclusa.");
        return await ExecuteWorkAsync(new(allowed,record.Path,read,list),token);
    }
    internal async Task<DocumentWorkReply> ExecuteWorkAsync(DocumentWork work,CancellationToken token)
    {
        if(!await slots.WaitAsync(0,token))throw new IOException("Due letture documentali già in corso. Riprovare su richiesta, senza accodare scansioni.");
        try{return await RunWorkerAsync(work,token);}finally{slots.Release();}
    }
    public static async Task<DocumentWorkReply> RunWorkerAsync(DocumentWork work,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var start=new ProcessStartInfo {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        // Il parser gira sotto l’account del servizio; tutte le aperture delle sorgenti sono in sola lettura.
        var assembly=typeof(DocumentService).Assembly.Location;
        var executable=Path.ChangeExtension(assembly,".exe");
        if(File.Exists(executable))start.FileName=executable;
        else{start.FileName="dotnet";start.ArgumentList.Add(assembly);}
        start.ArgumentList.Add("--document-worker");
        using var process=Process.Start(start)??throw new IOException("Impossibile avviare lettura documento.");
        var output=ReadBounded(process.StandardOutput,2*1024*1024,deadline.Token);
        var errors=ReadBounded(process.StandardError,65536,deadline.Token);
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(work).AsMemory(),deadline.Token);process.StandardInput.Close();
            while(!process.HasExited)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if(output.IsFaulted)await output;if(errors.IsFaulted)await errors;
                process.Refresh();
                if(process.PrivateMemorySize64>512L*1024*1024)throw new IOException("Documento troppo complesso: limite memoria raggiunto.");
                await Task.Delay(100,deadline.Token);
            }
            var json=await output;await errors;
            if(process.ExitCode!=0)throw new IOException("Il parser del documento si è interrotto.");
            var result=JsonSerializer.Deserialize<DocumentWorkReply>(json)??throw new IOException("Risposta documento vuota.");
            if(result.Error!=null)throw new IOException(result.Error);return result;
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested){throw new IOException("Lettura documento interrotta dopo 20 secondi. Nessuna elaborazione prosegue in background.");}
        finally
        {
            if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync(CancellationToken.None);}
            // Osserva anche gli errori dei flussi dopo annullamento, senza salvare il testo.
            try{await Task.WhenAll(output,errors);}catch{ }
        }
    }
    private static async Task<string> ReadBounded(StreamReader reader,int maximum,CancellationToken token)
    {var result=new StringBuilder();var buffer=new char[4096];int n;while((n=await reader.ReadAsync(buffer,token))>0){if(result.Length+n>maximum)throw new IOException("Risposta parser oltre limite.");result.Append(buffer,0,n);}return result.ToString();}
}
