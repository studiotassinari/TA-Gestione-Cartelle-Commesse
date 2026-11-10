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
        check(new ConfigurationFile().Validate().Count > 0 && ConfigurationFile.Parse(updated.Merge(null)).Complete && ConfigurationFile.Parse(updated.Merge("non json")).Complete,
            "Prima configurazione: file assente o illeggibile sostituito da una configurazione completa");
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
