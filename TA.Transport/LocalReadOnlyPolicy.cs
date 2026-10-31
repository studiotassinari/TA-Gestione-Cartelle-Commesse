namespace TA.Transport;

// Verificata sia dal chiamante sia dal servizio: non dipende dalle annotazioni MCP.
public static class LocalReadOnlyPolicy
{
    public static void Validate(Request request)
    {
        if (request.Version != 2 || request.Operation is not ("Info" or "State" or "Children" or "DailyReport" or "Analysis" or "AnalysisReport" or "Files" or "Progress"))
            throw new UnauthorizedAccessException("Il canale MCP locale consente esclusivamente consultazioni.");
        if (request.Category is not ("C" or "G" or "A")) throw new ArgumentException("Categoria non valida.");
    }
}
