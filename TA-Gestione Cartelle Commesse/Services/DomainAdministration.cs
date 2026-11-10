namespace TA_Gestione_Cartelle_Commesse.Services;
// L'abilitazione arriva dal server, che confronta il SID autenticato con gli utenti di gestione
// straordinaria della propria configurazione: nel client non è scritto alcun nome.
// Serve solo a mostrare i comandi; ogni operazione viene verificata di nuovo dal server.
public static class DomainAdministration
{
    private static volatile bool enabled;
    public static bool IsEnabled => enabled;
    public static void Set(bool value) => enabled = value;
}
