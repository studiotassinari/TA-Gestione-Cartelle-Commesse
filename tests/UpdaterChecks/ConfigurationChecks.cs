using System.Text.Json;
using TA.ServiceManager;

// Dati sintetici: nessun dominio, gruppo o utente reale.
internal static class ConfigurationChecks
{
    public static void Run(Action<bool, string> check)
    {
        const string previous = """{"Server":{"AllowedGroup":"ESEMPIO\\Utenti","Port":6001,"EnableMcpIpripiWrites":true,"DatabaseFolder":"D:\\Dati","JobsRoot":"\\\\server.example\\dati\\lavori"},"Logging":{"LogLevel":{"Default":"Information"}}}""";
        var old = ConfigurationFile.Parse(previous);
        check(old.Groups.SequenceEqual(new[] { @"ESEMPIO\Utenti" }) && old.Port == 6001 && old.ManagementUsers.Length == 0, "Configurazione precedente con un solo gruppo letta senza utenti di gestione");
        check(!old.Complete, "Configurazione precedente senza tutti i percorsi proposta per il completamento");
        var updated = new ConfigurationFile
        {
            Domain = "esempio.example", Groups = new[] { @"ESEMPIO\Utenti", @"ESEMPIO\Tecnici" }, CollaboratorsGroup = @"ESEMPIO\Collaboratori",
            ManagementUsers = ConfigurationFile.Split("responsabile.uno@esempio.example\r\n responsabile.due@esempio.example ;RESPONSABILE.UNO@esempio.example"), Port = 6001,
            JobsRoot = @"\\server.example\dati\lavori", BidsRoot = @"\\server.example\dati\gare", ArchivedJobsRoot = @"\\server.example\dati\archivio", ArchiveFolder = @"D:\Dati\Runtime"
        };
        check(updated.ManagementUsers.Length == 2 && updated.Complete, "Utenti di gestione separati per riga o punto e virgola, senza duplicati");
        var merged = updated.Merge(previous);
        using var document = JsonDocument.Parse(merged); var server = document.RootElement.GetProperty("Server");
        check(server.GetProperty("EnableMcpIpripiWrites").GetBoolean() && server.GetProperty("DatabaseFolder").GetString() == @"D:\Dati" && document.RootElement.TryGetProperty("Logging", out _),
            "Il salvataggio conserva le impostazioni non mostrate nella finestra");
        check(server.GetProperty("AllowedGroups").GetArrayLength() == 2 && server.GetProperty("AllowedGroup").GetString() == @"ESEMPIO\Utenti",
            "Più gruppi autorizzati salvati; il primo resta nella proprietà storica");
        var reread = ConfigurationFile.Parse(merged);
        check(reread.Groups.Length == 2 && reread.Domain == "esempio.example" && reread.CollaboratorsGroup == @"ESEMPIO\Collaboratori" && reread.ManagementUsers.SequenceEqual(updated.ManagementUsers),
            "Dominio, gruppi e utenti di gestione riletti come salvati");
        // Database comune: sempre account SQL, password con caratteri particolari conservata, account Windows rifiutato.
        var sql = new SqlAccount(@".\SQLEXPRESS", "ProvaComune", "utente_prova", "pa;ss \"wo=rd");
        ConfigurationFile Shared(string connection) => new()
        {
            Domain = updated.Domain, Groups = updated.Groups, CollaboratorsGroup = updated.CollaboratorsGroup, ManagementUsers = updated.ManagementUsers, Port = updated.Port,
            JobsRoot = updated.JobsRoot, BidsRoot = updated.BidsRoot, ArchivedJobsRoot = updated.ArchivedJobsRoot, ArchiveFolder = updated.ArchiveFolder, SharedDatabase = connection, AutomaticGeocoding = connection.Length > 0
        };
        check(ConfigurationFile.Parse(Shared("Server=s;Database=d;User ID=u;Password=p").Merge(previous)).AutomaticGeocoding && !ConfigurationFile.Parse(Shared("").Merge(previous)).AutomaticGeocoding && !old.AutomaticGeocoding,
            "Ricerca automatica della posizione salvata e riletta; disattivata se non scelta");
        var saved = ConfigurationFile.Parse(Shared(sql.ToConnectionString()).Merge(previous));
        check(SqlAccount.Parse(saved.SharedDatabase) == sql && saved.Validate().Count == 0 && saved.SharedDatabase.EndsWith("TrustServerCertificate=True"), "Account SQL del database comune salvato e riletto, password compresa");
        check(SqlAccount.Parse("Data Source=srv;Initial Catalog=db;UID=u;PWD='a;b'") == new SqlAccount("srv", "db", "u", "a;b") && new SqlAccount().ToConnectionString() == "" && SqlAccount.Parse(null).Empty, "Stringhe di connessione equivalenti lette; campi vuoti = nessun collegamento");
        check(Shared("Server=srv;Database=db;Integrated Security=True").Validate().Count >= 2 && Shared(new SqlAccount("srv", "db", "u").ToConnectionString()).Validate().Count == 1
            && ConfigurationFile.Parse(Shared("").Merge(saved.Merge(previous))).SharedDatabase == "", "Collegamento con account Windows o senza password rifiutato; campi svuotati tolgono il collegamento");
        // Indirizzo di GeCo: salvato e riletto, tolto se svuotato, rifiutato se non è un indirizzo http o https.
        ConfigurationFile WithGeCo(string address) => new()
        {
            Domain = updated.Domain, Groups = updated.Groups, CollaboratorsGroup = updated.CollaboratorsGroup, ManagementUsers = updated.ManagementUsers, Port = updated.Port,
            JobsRoot = updated.JobsRoot, BidsRoot = updated.BidsRoot, ArchivedJobsRoot = updated.ArchivedJobsRoot, ArchiveFolder = updated.ArchiveFolder, GeCoAddress = address
        };
        var withGeCo = WithGeCo("http://server.example:8027").Merge(previous);
        check(ConfigurationFile.Parse(withGeCo).GeCoAddress == "http://server.example:8027" && WithGeCo("http://server.example:8027").Validate().Count == 0
            && ConfigurationFile.Parse(WithGeCo("").Merge(withGeCo)).GeCoAddress == "" && old.GeCoAddress == "", "Indirizzo di GeCo salvato e riletto; svuotato viene tolto");
        check(WithGeCo("server.example:8027").Validate().Count == 1 && WithGeCo(@"C:\programma.exe").Validate().Count == 1 && WithGeCo("file://server/cartella").Validate().Count == 1,
            "Indirizzo di GeCo accettato solo se http o https");
        check(new ConfigurationFile().Validate().Count > 0 && ConfigurationFile.Parse(updated.Merge(null)).Complete && ConfigurationFile.Parse(updated.Merge("non json")).Complete,
            "Prima configurazione: file assente o illeggibile sostituito da una configurazione completa");
        using (var without = JsonDocument.Parse(updated.Merge("""{"Server":{"JobTemplate":""}}""")))
            check(!without.RootElement.GetProperty("Server").TryGetProperty("JobTemplate", out _), "Cartella modello vuota non salvata: resta valido il modello predefinito del servizio");
        using (var kept = JsonDocument.Parse(new ConfigurationFile { Groups = updated.Groups, JobTemplate = @"\\server.example\dati\modello" }.Merge(null)))
            check(kept.RootElement.GetProperty("Server").GetProperty("JobTemplate").GetString() == @"\\server.example\dati\modello" && !kept.RootElement.GetProperty("Server").TryGetProperty("BidTemplate", out _), "Cartella modello indicata salvata; modello delle gare vuoto non salvato");
        check(ConfigurationFile.Parse(new ConfigurationFile { Groups = updated.Groups, BidTemplate = @"\\server.example\dati\modello gare" }.Merge(null)).BidTemplate == @"\\server.example\dati\modello gare", "Cartella modello delle gare salvata e riletta");
        check(!reread.LocalAnalysis && reread.LocalAnalysisStartHour == 20 && reread.LocalAnalysisEndHour == 7 && reread.LocalAnalysisDocumentsPerHour == 30,
            "Analisi automatica locale disattivata se non scelta, con limiti prudenti predefiniti");
        var automatic = ConfigurationFile.Parse(new ConfigurationFile { Groups = updated.Groups, JobsRoot = updated.JobsRoot, BidsRoot = updated.BidsRoot, ArchivedJobsRoot = updated.ArchivedJobsRoot, ArchiveFolder = "x",
            LocalAnalysis = true, LocalAnalysisStartHour = 22, LocalAnalysisEndHour = 5, LocalAnalysisDocumentsPerHour = 12 }.Merge(merged));
        check(automatic.LocalAnalysis && automatic.LocalAnalysisStartHour == 22 && automatic.LocalAnalysisEndHour == 5 && automatic.LocalAnalysisDocumentsPerHour == 12, "Limiti dell'analisi automatica salvati e riletti");
        check(new ConfigurationFile { Groups = updated.Groups, JobsRoot = updated.JobsRoot, BidsRoot = updated.BidsRoot, ArchivedJobsRoot = updated.ArchivedJobsRoot, ArchiveFolder = "x",
            LocalAnalysisStartHour = 24, LocalAnalysisDocumentsPerHour = 500 }.Validate().Count == 2, "Ore e ritmo dell'analisi automatica fuori dai limiti rifiutati");
        check(new ConfigurationFile { Groups = updated.Groups, Port = 8080, JobsRoot = @"C:\lavori", BidsRoot = updated.BidsRoot, ArchivedJobsRoot = updated.ArchivedJobsRoot, ArchiveFolder = "x" }.Validate().Count == 2,
            "Porta web e percorso non UNC rifiutati");
    }
}
