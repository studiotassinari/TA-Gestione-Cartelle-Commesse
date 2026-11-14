using GeCo.Dati;
using Microsoft.Data.SqlClient;
using TA.Server.SharedDatabase;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Models;

// Database comune con GeCo: conversione degli elenchi in cartelle rilevate e, se è disponibile un SQL Server di prova
// (variabile TA_TEST_SQL, impostata dai flussi di GitHub su LocalDB), scrittura reale nella tabella tblCartelle.
internal static class SharedDatabaseChecks
{
    public static void Run()
    {
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        var checkedUtc = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
        var snapshot = new ArchiveSnapshot
        {
            Prefix = 'C',
            Records =
            [
                new FolderRecord("C24031", "24", "031", " Committente di prova ", "Luogo - Oggetto", @"\\server\lavori\2024\C24031 - Committente di prova - Luogo - Oggetto", "Attiva")
                    { Inspection = new FolderInspection { TotalBytes = 2048, FileCount = 7, LastFileWriteUtc = checkedUtc, QuoteStatus = "Presente" } },
                new FolderRecord("C23007", "23", "007", "", "", @"\\server\lavori\2023\C23007", "Compressa") { ArchiveBytes = 512, ArchiveModifiedUtc = checkedUtc },
                new FolderRecord("", "", "", "", "", @"\\server\lavori\senza-codice", "")
            ]
        };
        var rows = SharedFolders.From(snapshot);
        Check(rows.Length == 2 && rows[0] is { Codice: "C24031", Anno: 2024, Committente: "Committente di prova", Byte: 2048, File: 7, Preventivo: "Presente" }
            && rows[1] is { Codice: "C23007", Anno: 2023, Committente: null, Oggetto: null, Byte: 512, Stato: "Compressa" } && rows[1].UltimaModifica != null,
            "Elenco delle cartelle non convertito per il database comune");
        Check(SharedFolders.Year("7") == 2007 && SharedFolders.Year("2019") == 2019 && SharedFolders.Year("x") == null, "Anno della cartella non interpretato");

        var sql = Environment.GetEnvironmentVariable("TA_TEST_SQL");
        if (string.IsNullOrWhiteSpace(sql)) { Console.WriteLine("SharedDatabaseChecks: conversione verificata; scrittura su SQL Server non provata (TA_TEST_SQL non impostata)."); return; }

        var states = new[] { new CategoryState('C', "", false, "", "", snapshot), new CategoryState('G', "", false, "", "", null) };
        string first = SharedFolders.PushAsync(sql, states, CancellationToken.None).GetAwaiter().GetResult();
        Check(first.Contains("C: 2 cartelle") && !first.Contains("G:"), "Prima sincronizzazione non riuscita: " + first);
        // Seconda scansione: una cartella sparisce e una cambia stato. Nessun duplicato, la sparita resta segnata come non presente.
        var later = snapshot with { Records = [snapshot.Records[0] with { Status = "Archiviata" }] };
        SharedFolders.PushAsync(sql, new[] { new CategoryState('C', "", false, "", "", later) }, CancellationToken.None).GetAwaiter().GetResult();
        using var connection = new SqlConnection(sql); connection.Open();
        int Count(string where) { using var command = new SqlCommand("SELECT COUNT(*) FROM dbo.tblCartelle WHERE " + where, connection); return (int)command.ExecuteScalar()!; }
        Check(Count("1 = 1") == 2 && Count("Car_Presente = 1") == 1 && Count("Car_Codice = 'C24031' AND Car_Stato = 'Archiviata' AND Car_Presente = 1 AND Car_Anno = 2024") == 1
            && Count("Car_Codice = 'C23007' AND Car_Presente = 0") == 1, "Seconda sincronizzazione: cartelle duplicate o stato non aggiornato");
        using (var command = new SqlCommand("SELECT Cas_Cartelle FROM dbo.tblCartelleSincronizzazioni WHERE Cas_Categoria = 'C'", connection))
            Check((int)command.ExecuteScalar()! == 1, "Registro delle sincronizzazioni non aggiornato");
        Check(AggiornamentoDatabase.VersioneAttualeAsync(sql).GetAwaiter().GetResult() >= SincronizzazioneCartelle.VersioneRichiesta, "Database comune non aggiornato alla versione richiesta");
        Console.WriteLine("SharedDatabaseChecks: aggiornamento dello schema e sincronizzazione delle cartelle verificati su SQL Server.");
    }
}
