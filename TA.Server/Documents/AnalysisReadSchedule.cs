namespace TA.Server.Documents;

// Limite condiviso tra geologiche e MUR. Non avvia alcuna lettura autonomamente.
public sealed record AnalysisReadState(string DocumentId,DateTime NextUtc);
public sealed class AnalysisReadSchedule(Func<DateTime>? clock=null,Func<AnalysisReadState?>? load=null,Action<AnalysisReadState>? save=null)
{
    private readonly object gate=new();
    private string current="";
    private DateTime next;
    private bool busy;
    private bool loaded;
    public IDisposable Enter(string id)
    {
        lock(gate)
        {
            if(!loaded){var state=load?.Invoke();if(state!=null){current=state.DocumentId;next=state.NextUtc;}loaded=true;}
            var now=clock?.Invoke()??DateTime.UtcNow;
            if(busy)throw new IOException("Analisi documentale occupata: attendere la conclusione del file in lettura.");
            if(current!=id&&now<next)throw new IOException($"Analisi documentale: un nuovo file al minuto. Riprovare fra {Math.Ceiling((next-now).TotalSeconds)} secondi; completare e salvare prima il documento corrente.");
            if(current!=id){var state=new AnalysisReadState(id,now.AddMinutes(1));save?.Invoke(state);current=id;next=state.NextUtc;}
            busy=true;return new Lease(this);
        }
    }
    private sealed class Lease(AnalysisReadSchedule owner):IDisposable
    {
        private bool disposed;
        public void Dispose(){lock(owner.gate){if(disposed)return;disposed=true;owner.busy=false;}}
    }
}
