namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Percentuale di unità elaborate nella fase corrente, non una stima del tempo residuo.
public sealed record ScanProgress(bool IsRunning, string Phase, int Completed, int? Total, string Message,
    DateTime? StartedUtc = null, DateTime? FinishedUtc = null, DateTime? NextRunUtc = null, bool HasErrors = false)
{
    public double? Percentage => Total == null ? null : Total == 0 ? 100 : Math.Clamp(Math.Round(100.0 * Completed / Total.Value, 1), 0, 100);
}
