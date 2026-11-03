namespace TA_Gestione_Cartelle_Commesse.Services;

// I messaggi restano nell'interfaccia: nessun registro automatico su disco o in rete.
// Mantiene il punto di chiamata per i componenti del client senza avviare writer in background.
public static class ClientLog
{
    public static void Write(string message, Exception? error = null) { }
}
