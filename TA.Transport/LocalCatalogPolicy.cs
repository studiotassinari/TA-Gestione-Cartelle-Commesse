namespace TA.Transport;

// La policy storica resta strettamente in sola lettura; questa estensione ammette una sola scrittura tipizzata.
public static class LocalCatalogPolicy
{
    public static void Validate(Request request)
    {
        var expected = request.Operation switch {
            "PendingGeologicalAnalyses" => "prossime_relazioni_da_analizzare",
            "SaveGeologicalAnalysis" => "salva_analisi_geologica",
            "GeologicalDossiers" => "elenca_fascicoli_geologici",
            _ => null
        };
        if (expected == null) { LocalReadOnlyPolicy.Validate(request); return; }
        if (request.Version != 2 || request.McpTool != expected || !Guid.TryParseExact(request.CorrelationId, "N", out _) || request.Category is not ("C" or "G" or "A"))
            throw new UnauthorizedAccessException("Comando di catalogazione non ammesso fuori da una richiesta MCP esplicita.");
    }
}
