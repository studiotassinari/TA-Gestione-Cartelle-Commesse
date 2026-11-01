using System.Security.Cryptography;
namespace TA.Mcp;

// Nessuna attività di fondo: una consultazione scade dopo due minuti o venti operazioni.
public sealed class ConsultationSessions(Func<DateTimeOffset>? utcNow=null)
{
    private sealed record Session(DateTimeOffset Expires,bool Contents){public int Remaining=20;}
    private readonly Func<DateTimeOffset> now=utcNow??(()=>DateTimeOffset.UtcNow);
    private readonly Dictionary<string,Session> sessions=new();
    private readonly object gate=new();
    public static ConsultationSessions Shared {get;}=new();
    public object Open(bool contents)
    {
        lock(gate)
        {
            foreach(var key in sessions.Where(p=>p.Value.Expires<=now()||p.Value.Remaining==0).Select(p=>p.Key).ToArray())sessions.Remove(key);
            if(sessions.Count>=32)throw new InvalidOperationException("Limite consultazioni attive raggiunto. Nessuna apertura automatica in ciclo.");
            var id=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));var expires=now().AddMinutes(2);
            sessions.Add(id,new(expires,contents));return new {consultazione=id,scadeUtc=expires,operazioniMassime=20,contenutiConsentiti=contents,note="Usare esclusivamente per la richiesta corrente dell'utente. Non rinnovare automaticamente, non eseguire monitoraggio o indicizzazione."};
        }
    }
    public void Use(string id,bool contents)
    {
        lock(gate)
        {
            if(!sessions.TryGetValue(id,out var session)||session.Expires<=now()||session.Remaining<=0)throw new InvalidOperationException("Consultazione assente, scaduta o esaurita. Solo per una nuova richiesta esplicita dell'utente usare inizia_consultazione. Nessun rinnovo automatico.");
            if(contents&&!session.Contents)throw new InvalidOperationException("Questa consultazione consente solo nomi e metadati. La lettura dei contenuti richiede una richiesta esplicita dell'utente.");
            session.Remaining--;
        }
    }
}
