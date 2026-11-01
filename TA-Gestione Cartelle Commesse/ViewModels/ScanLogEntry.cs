namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// Una riga del riepilogo osservato dal client, non un registro completo di tutti gli accessi.
public sealed record ScanLogEntry(string Time, string Category, string Phase, string Path);
