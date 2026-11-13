namespace TA_Gestione_Cartelle_Commesse.ViewModels;
// Sintesi dell'esito sul preventivo calcolato dal server dai soli nomi dei file nella cartella amministrazione:
// è un'indicazione da verificare, non la conferma che il preventivo esista o sia stato accettato.
public static class QuoteIndicator
{
    // 0 = presente, 1 = da verificare, 2 = non trovato, 3 = non ancora verificato.
    public static int Level(string? status) =>
        string.IsNullOrWhiteSpace(status) || status == "Non verificato" ? 3
        : status.StartsWith("Presente", StringComparison.OrdinalIgnoreCase) ? 0
        : status.StartsWith("Da verificare", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
    public static string Label(string? status) => Level(status) switch { 0 => "Presente", 1 => "Da verificare", 2 => "Non trovato", _ => "Non verificato" };
}
