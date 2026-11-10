namespace TA.Server;

// Limiti all'uso di risorse dell'analisi automatica locale. Tutti i valori sono ricondotti a intervalli prudenti:
// una configurazione errata non può trasformare l'analisi in un carico continuo.
public sealed record LocalAnalysisLimits(bool Enabled, int StartHour, int EndHour, int DocumentsPerHour)
{
    public static LocalAnalysisLimits From(ServerSettings settings) => new(settings.EnableLocalAnalysis,
        Math.Clamp(settings.LocalAnalysisStartHour, 0, 23), Math.Clamp(settings.LocalAnalysisEndHour, 0, 23), Math.Clamp(settings.LocalAnalysisDocumentsPerHour, 1, 120));
    // Pausa minima tra l'inizio di un documento e il successivo.
    public TimeSpan Pause => TimeSpan.FromSeconds(3600.0 / DocumentsPerHour);
    // Ore italiane. Inizio uguale a fine: tutto il giorno. La fascia può attraversare la mezzanotte (20 → 7).
    public bool InWindow(DateTime italianTime)
    {
        int hour = italianTime.Hour;
        return StartHour == EndHour || (StartHour < EndHour ? hour >= StartHour && hour < EndHour : hour >= StartHour || hour < EndHour);
    }
    public static DateTime ItalianTime(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"));
    // Attesa prima del prossimo documento e motivo; zero = si può procedere.
    public (TimeSpan Delay, string Reason) Wait(DateTime utc, DateTime? lastStartedUtc, bool inventoryScanRunning, bool onDemandReading)
    {
        if (!Enabled) return (TimeSpan.FromMinutes(5), "Analisi automatica locale disattivata");
        if (inventoryScanRunning) return (TimeSpan.FromMinutes(1), "In pausa: scansione dell'inventario in corso");
        if (onDemandReading) return (TimeSpan.FromSeconds(30), "In pausa: lettura su richiesta in corso");
        if (!InWindow(ItalianTime(utc))) return (TimeSpan.FromMinutes(5), $"In pausa: fuori dalla fascia oraria {StartHour:00}:00–{EndHour:00}:00");
        if (lastStartedUtc is { } last && utc - last < Pause) return (Pause - (utc - last), "Pausa tra un documento e il successivo");
        return (TimeSpan.Zero, "");
    }
    public string Describe() => !Enabled ? "Su richiesta ChatGPT; nessuna analisi cloud automatica"
        : $"Analisi automatica locale attiva, senza servizi esterni: {(StartHour == EndHour ? "tutto il giorno" : $"dalle {StartHour:00}:00 alle {EndHour:00}:00")}, al massimo {DocumentsPerHour} documenti l'ora, uno alla volta a priorità ridotta; in pausa durante scansioni e letture su richiesta";
}
