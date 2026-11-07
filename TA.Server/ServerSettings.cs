namespace TA.Server;

// I percorsi appartengono al server e non possono essere cambiati tramite richieste del client.
public sealed class ServerSettings
{
    public string JobsRoot { get; set; } = "";
    public string BidsRoot { get; set; } = ""; public string ArchivedJobsRoot { get; set; } = @"\\ingtassinari.it\data\archivio";
    public string ArchiveFolder { get; set; } = "";
    public string DatabaseFolder { get; set; } = "";
    public bool LocalStorageOnly { get; set; }
    public string BackupFolder { get; set; } = "";
    public string LegacyCatalogFolder { get; set; } = "";
    public string LegacyDatabaseFolder { get; set; } = "";
    public string BackupDirectory => string.IsNullOrWhiteSpace(BackupFolder) ? Path.Combine(ArchiveFolder,"backup-database") : Path.GetFullPath(BackupFolder);
    public string InventoryFolder
    {
        get
        {
            var target=Path.GetFullPath(string.IsNullOrWhiteSpace(DatabaseFolder)?ArchiveFolder:DatabaseFolder);
            foreach(var source in new[]{JobsRoot,BidsRoot,ArchivedJobsRoot}.Where(p=>!string.IsNullOrWhiteSpace(p)))
            {
                var root=Path.GetFullPath(source).TrimEnd('\\','/');
                if(target.Equals(root,StringComparison.OrdinalIgnoreCase)||target.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Il database deve essere esterno alle cartelle originali.");
            }
            return target;
        }
    }
    public int MaxConcurrentScans { get; set; } = 2;
    public int BackupRetention { get; set; } = 7;
    public string GeologyCatalogPath { get; set; } = "";
    // Modificabile dall'amministratore per passare a un'istanza Nominatim aziendale senza aggiornare il client.
    public string NominatimSearchUrl { get; set; } = "https://nominatim.openstreetmap.org/search";
    // Abilitazione esplicita: l'unica scrittura MCP riguarda il catalogo JSON derivato.
    public bool EnableMcpGeologyWrites { get; set; }
    public bool EnableMcpIpripiWrites { get; set; }
    // Un gruppo esplicito evita di esporre tutti i metadati a qualunque account del dominio.
    public string JobTemplate { get; set; } = @"\\ingtassinari.it\data\lavori\2026\C26198 - CLIENTE - LUOGO - COSA";
    public string AllowedGroup { get; set; } = "";
    public string CollaboratorsGroup { get; set; } = "GG_TA_Commesse_Users";
    // Compatibilità con configurazioni precedenti: la frequenza effettiva è oraria.
    public int RefreshMinutes { get; set; } = 60;
    // Porta TCP dedicata: i valori comunemente usati per HTTP sono esclusi.
    public int Port { get; set; } = 5443;

}
