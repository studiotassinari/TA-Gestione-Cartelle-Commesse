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
    public int BackupRetention { get; set; } = 5;
    public string GeologyCatalogPath { get; set; } = "";
    // Modificabile dall'amministratore per passare a un'istanza Nominatim aziendale senza aggiornare il client.
    public string NominatimSearchUrl { get; set; } = "https://nominatim.openstreetmap.org/search";
    // Abilitazione esplicita: l'unica scrittura MCP riguarda il catalogo JSON derivato.
    public bool EnableMcpGeologyWrites { get; set; }
    public bool EnableMcpIpripiWrites { get; set; }
    // Un gruppo esplicito evita di esporre tutti i metadati a qualunque account del dominio.
    // Modello per le nuove gare; vuoto = viene creata solo la cartella principale, senza sottocartelle.
    public string BidTemplate { get; set; } = "";
    // Modello Word (.docx o .dotx) con intestazione e stili dello studio, usato dall'esportazione del curriculum; vuoto = impaginazione neutra.
    public string CurriculumTemplate { get; set; } = "";
    public string JobTemplate { get; set; } = @"\\ingtassinari.it\data\lavori\2026\C26198 - CLIENTE - LUOGO - COSA";
    // AllowedGroup resta per le configurazioni precedenti; i nuovi salvataggi usano AllowedGroups.
    public string AllowedGroup { get; set; } = "";
    public string[] AllowedGroups { get; set; } = [];
    // Dominio Active Directory dell'installazione; vuoto = dominio a cui appartiene il server.
    public string Domain { get; set; } = "";
    // Vuoto = nessun gruppo collaboratori: accesso solo tramite i gruppi autorizzati.
    public string CollaboratorsGroup { get; set; } = "";
    // Account (utente@dominio o DOMINIO\utente) ammessi alla gestione straordinaria: servizio, registri, archiviazione ZIP, gestionale.
    public string[] ManagementUsers { get; set; } = [];
    public string[] AuthorizedGroups() => (AllowedGroups ?? Array.Empty<string>()).Append(AllowedGroup).Select(g => (g ?? "").Trim()).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    // Analisi automatica locale dei candidati (testo e regole, nessun servizio esterno). Disattivata se non scelta in Configura.
    public bool EnableLocalAnalysis { get; set; }
    // Limiti all'uso di risorse: fascia oraria italiana (inizio = fine significa tutto il giorno) e documenti l'ora.
    public int LocalAnalysisStartHour { get; set; } = 20;
    public int LocalAnalysisEndHour { get; set; } = 7;
    public int LocalAnalysisDocumentsPerHour { get; set; } = 30;
    // Compatibilità con configurazioni precedenti: la frequenza effettiva è oraria.
    public int RefreshMinutes { get; set; } = 60;
    // Porta TCP dedicata: i valori comunemente usati per HTTP sono esclusi.
    public int Port { get; set; } = 5443;

}
