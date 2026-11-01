namespace TA.Transport;

// Verificata sia dal chiamante sia dal servizio: non dipende dalle annotazioni MCP.
public static class LocalReadOnlyPolicy
{
    public static void Validate(Request request)
    {
        if (request.Version != 2 || request.Operation is not ("Info" or "State" or "Children" or "DailyReport" or "Analysis" or "Files" or "Progress" or "ReadDocument" or "ListDocuments"))
            throw new UnauthorizedAccessException("Il canale MCP locale consente esclusivamente consultazioni.");
        if(request.Operation!="Info")
        {
            var expected=request.Operation switch { "State"=>new[]{"cerca_cartelle","dettaglio_cartella"},"Children"=>["sottocartelle"],"Progress"=>["stato_scansione"],"Files"=>["cerca_file"],"Analysis"=>["analisi_file"],"DailyReport"=>["report_giornaliero"],"ReadDocument"=>["leggi_documento"],"ListDocuments"=>["elenca_documenti"],_=>Array.Empty<string>()};
            if(request.McpTool==null || !expected.Contains(request.McpTool) || !Guid.TryParseExact(request.CorrelationId,"N",out _))
                throw new UnauthorizedAccessException("Aggiornare il componente TA.Mcp: accesso consentito solo durante una consultazione esplicita del plugin.");
        }
        if (request.Category is not ("C" or "G" or "A")) throw new ArgumentException("Categoria non valida.");
    }
}
