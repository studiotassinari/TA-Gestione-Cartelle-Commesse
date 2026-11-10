using System.Diagnostics;
namespace TA.Mcp;

// Un accesso alla sorgente richiede una chiamata strumento ancora attiva.
public sealed class McpInvocation : IDisposable
{
    // Il contesto segue la singola catena asincrona: richieste concorrenti non si scambiano gli ID.
    private static readonly AsyncLocal<McpInvocation?> current=new();
    private readonly McpInvocation? previous;
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private bool active=true;
    public string Id {get;}=Guid.NewGuid().ToString("N");
    public string Tool {get;}
    private McpInvocation(string tool){Tool=tool;previous=current.Value;current.Value=this;Console.Error.WriteLine($"MCP richiesta {Id}: {Tool} avviata");}
    public static McpInvocation Begin(string tool,string consultation) { try { ConsultationSessions.Shared.Use(consultation,tool is "leggi_documento" or "leggi_relazione_geologica" or "salva_analisi_geologica" or "leggi_mur_ipripi" or "salva_analisi_ipripi", tool == "salva_analisi_geologica", tool == "salva_analisi_ipripi"); } catch(InvalidOperationException ex) { throw new ModelContextProtocol.McpException(ex.Message); } return new(tool); }
    /// <summary>Associa strumento e correlazione alla richiesta per i controlli lato server.</summary>
    /// <remarks>Il contesto locale non sostituisce autenticazione e autorizzazione del servizio.</remarks>
    public static TA.Transport.Request Tag(TA.Transport.Request request)
    {
        var call=current.Value;
        if(call is not {active:true})throw new InvalidOperationException("Accesso ai dati consentito solo durante una richiesta esplicita al plugin.");
        return request with {CorrelationId=call.Id,McpTool=call.Tool};
    }
    public void Dispose(){active=false;current.Value=previous;Console.Error.WriteLine($"MCP richiesta {Id}: {Tool} conclusa in {clock.ElapsedMilliseconds} ms; {(Tool is "approfondisci_commessa"?"eventuale lavoro esplicito consultabile con il suo ID":"nessuna attività accodata")}");}
}
