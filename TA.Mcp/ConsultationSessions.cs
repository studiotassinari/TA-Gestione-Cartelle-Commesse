using System.Security.Cryptography;
namespace TA.Mcp;

// Nessuna attività di fondo: una consultazione scade dopo due minuti o venti operazioni.
public sealed class ConsultationSessions(Func<DateTimeOffset>? utcNow=null)
{
    private sealed record Session(DateTimeOffset Expires,bool Contents,bool Analysis){public int Remaining=Analysis?60:20;}
    private readonly Func<DateTimeOffset> now=utcNow??(()=>DateTimeOffset.UtcNow);
    private readonly Dictionary<string,Session> sessions=new();
    private readonly object gate=new();
    public static ConsultationSessions Shared {get;}=new();
    public object Open(bool contents,bool analysis=false)
    {
        lock(gate)
        {
            foreach(var key in sessions.Where(p=>p.Value.Expires<=now()||p.Value.Remaining==0).Select(p=>p.Key).ToArray())sessions.Remove(key);
            if(sessions.Count>=32)throw new InvalidOperationException("Limite consultazioni attive raggiunto. Nessuna apertura automatica in ciclo.");
            var id=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));var expires=now().AddMinutes(analysis?15:2);
            if(analysis&&!contents)throw new ArgumentException("La catalogazione richiede lettura esplicita dei contenuti.");
            sessions.Add(id,new(expires,contents,analysis));return new {consultazione=id,scadeUtc=expires,operazioniMassime=analysis?60:20,contenutiConsentiti=contents,salvataggioAnalisiConsentito=analysis,note="Usare esclusivamente per la richiesta corrente dell'utente. Non rinnovare automaticamente, non eseguire monitoraggio o indicizzazione."};
        }
    }
    public void Use(string id,bool contents,bool analysis=false)
    {
        lock(gate)
        {
            if(!sessions.TryGetValue(id,out var session)||session.Expires<=now()||session.Remaining<=0)throw new InvalidOperationException("Consultazione assente, scaduta o esaurita. Solo per una nuova richiesta esplicita dell'utente usare inizia_consultazione. Nessun rinnovo automatico.");
            if(contents&&!session.Contents)throw new InvalidOperationException("Questa consultazione consente solo nomi e metadati. La lettura dei contenuti richiede una richiesta esplicita dell'utente.");
            if(analysis&&!session.Analysis)throw new InvalidOperationException("Salvataggio non consentito: la catalogazione deve essere richiesta esplicitamente con analisiGeologiche=true.");
            session.Remaining--;
        }
    }
}
