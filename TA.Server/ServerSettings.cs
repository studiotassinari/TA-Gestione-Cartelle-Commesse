namespace TA.Server;

// I percorsi appartengono al server e non possono essere cambiati tramite richieste del client.
public sealed class ServerSettings
{
    public string JobsRoot { get; set; } = "";
    public string BidsRoot { get; set; } = "";
    public string ArchiveFolder { get; set; } = "";
    // Un gruppo esplicito evita di esporre tutti i metadati a qualunque account del dominio.
    public string AllowedGroup { get; set; } = "";
    public int RefreshMinutes { get; set; } = 1;
    // Porta TCP dedicata: i valori comunemente usati per HTTP sono esclusi.
    public int Port { get; set; } = 5443;

}
