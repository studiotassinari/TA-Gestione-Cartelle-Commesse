using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
using TA_Gestione_Cartelle_Commesse.ViewModels;
using TA_Gestione_Cartelle_Commesse.Views;

// Modulo Allineamento (2.40.0) con un servizio simulato: chi lo vede, sezioni e filtro «Comprese archiviate», proposte sempre
// indicate come tali, comandi inviati solo dopo la conferma. Regole e permessi veri sono del servizio (GeCoChecks) e della libreria.
internal static class AlignmentClientChecks
{
    private sealed class Fake(Func<GeCoCommand, object> answer) : IGeCoGateway
    {
        public List<GeCoCommand> Sent { get; } = [];
        public Task<T> Send<T>(GeCoCommand command, CancellationToken token = default)
        {
            lock (Sent) Sent.Add(command);
            return Task.FromResult(JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(answer(command)))!);
        }
    }
    static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }

    public static void Run()
    {
        GeCoFolder F(int id, string name, bool archived = false) => new(id, @"\\server\lavori\2026\" + name, name, archived ? "A" : "C", name[..6], 2026, "Rossi", "Oggetto", true, "01/11/2026 08:00", archived);
        var open = new GeCoAlignmentProject(1, "C26020", 2026, "Ponte", 1, "Rossi Srl", "Rossi", "Aperta", "SenzaCartella", null, null, "", null, null, true, null);
        var closed = new GeCoAlignmentProject(2, "C24001", 2024, "Scuola", 1, "Rossi Srl", "Rossi", "Archiviata", "InArchivio", null, F(7, "C24001 - Rossi - Scuola.zip", true), "sicura", "stesso codice", null, false, "commessa chiusa");
        var linked = new GeCoAlignmentProject(3, "C26021", 2026, "Palestra", 1, "Rossi Srl", "Rossi", "Aperta", "Collegata", F(8, "C26021 - Rossi - Palestra"), null, "", null, null, false, "ha già una cartella");
        var report = new GeCoAlignment([open, closed, linked],
            [new(F(9, "C26030 - Rossi - Verifica"), [], "commessa", null, [new(1, "Rossi Srl", "Rossi", "sicura", "nome breve uguale al committente")]), new(F(10, "C23005 - Rossi - Vecchia.zip", true), [], null, "archiviata", [])],
            [], [new(1, "1", "Rossi Srl", null, "01234567890", null, [], new(1, "Rossi", "sicura", 3, 3, [], "committente di tutte le cartelle"), 3, [])], [], [], [],
            [new(1, "Rossi")], 2020);
        var local = new GeCoExternalCustomers([new("loc", "Studio Nuovo", "09876543210", null, "Via Uno 1", null, "SoloEsterno", null, [], "nessun cliente con la stessa P.IVA", null, 2)],
            [new(1, "1", "Rossi Srl", "Rossi", "01234567890", null, "Ravenna")]);
        var gateway = new Fake(c => c.Action switch
        {
            "Access" => new GeCoAccess(true, "", "DOM\\a", "admin", "Amministratore", DatabaseVersion: 14, Administrator: true, Links: true),
            "Alignment" => report, "ExternalCustomers" => local, "ConfirmShortNames" => 1,
            "RequestFolder" => new GeCoFolderRequest(5, "commessa", 1, "C", "C26020", "C26020 - Rossi - Ponte", "Creata", "Creata", "Cartella creata da GeCa", @"\\server\lavori\2026\C26020 - Rossi - Ponte", "19/11/2026 10:00", "Amministratore", "GeCa", "19/11/2026 10:00", "TA-Gestione", 3),
            "ImportCustomer" => new GeCoCustomerSummary(3, "3", "Studio Nuovo", null, "09876543210", null, "Forlì"),
            _ => true,
        });
        var vm = new AlignmentViewModel(gateway);
        vm.StartAsync().GetAwaiter().GetResult();
        Check(vm.Allowed && vm.Report != null && vm.Local != null && vm.Summary.Contains("1 aperte da sistemare, di cui 1 senza cartella"), "Allineamento: amministratore con il database alla migrazione 14, rapporto e clienti di GeCa caricati");
        Check(Equals(vm.Items().Single().Source, open), "Allineamento: da sistemare solo le commesse aperte senza la cartella giusta");
        vm.IncludeArchived = true;
        Check(vm.Items().Select(r => r.Source).SequenceEqual(new object[] { open, closed }) && vm.Items().Last().F.Contains("[archivio]") && vm.Items().Last().F.Contains("proposta sicura"),
            "Allineamento: «Comprese archiviate» mostra le commesse archiviate con la copia in archivio, indicata come proposta");
        vm.Section = AlignmentViewModel.Folders;
        Check(vm.Items().Count() == 2, "Allineamento: con «Comprese archiviate» anche le cartelle in archivio");
        vm.IncludeArchived = false;
        Check(vm.Items().Single() is { E: "commessa" } row && row.D.Contains("proposta sicura"), "Allineamento: cartelle da cui creare, clienti proposti indicati come proposte");
        vm.Section = AlignmentViewModel.External;
        Check(vm.Items().Single() is { C: "Solo in GeCa: da importare", E: "2" }, "Allineamento: cliente solo in GeCa da importare, con le commesse che lo citano");
        var questions = new List<string>(); vm.Ask = q => { questions.Add(q); return false; };
        int sent = gateway.Sent.Count;
        Check(!vm.ConfirmSafeShortNamesAsync().GetAwaiter().GetResult() && gateway.Sent.Count == sent && questions.Single().Contains("1 nomi brevi"), "Allineamento: senza conferma nessun comando");
        vm.Ask = _ => true;
        Check(vm.ConfirmSafeShortNamesAsync().GetAwaiter().GetResult() && gateway.Sent.Last(c => c.Action == "ConfirmShortNames").Allineamento!.NomiBrevi!.Single() == new GeCoShortName(1, "Rossi"),
            "Allineamento: nomi brevi sicuri confermati con l'anteprima vista");
        Check(vm.RequestFolderAsync(new("commessa", 1, null, "Ravenna"), "C26020 - Rossi - Ravenna - Ponte", now: true).GetAwaiter().GetResult()
            && gateway.Sent.Last(c => c.Action == "RequestFolder") is { Valore: true, Allineamento.Cartella: { Tipo: "commessa", Id: 1, Luogo: "Ravenna" } } && vm.Done.StartsWith("Cartella «C26020 - Rossi - Ponte» creata"),
            "Allineamento: «Crea subito» invia la richiesta con l'esecuzione immediata e mostra l'esito del servizio");
        Check(vm.ImportCustomerAsync(new("loc", "Studio Nuovo", "Via Uno 1", "47121", "Forlì", "FC", "09876543210", null)).GetAwaiter().GetResult() && vm.Done.Contains("Studio Nuovo"),
            "Allineamento: importazione in GeCo dopo la conferma");
        var collaborator = new AlignmentViewModel(new Fake(c => new GeCoAccess(true, "", "DOM\\b", "b", "Persona B", DatabaseVersion: 14, Links: true)));
        collaborator.StartAsync().GetAwaiter().GetResult();
        Check(!collaborator.Allowed && collaborator.Denied.Contains("riservato agli amministratori") && !collaborator.Items().Any(), "Allineamento: chi non è amministratore vede solo il motivo");
        var old = new AlignmentViewModel(new Fake(c => new GeCoAccess(true, "", "DOM\\a", "admin", "Amministratore", DatabaseVersion: 13, Administrator: true, Links: true)));
        old.StartAsync().GetAwaiter().GetResult();
        Check(!old.Allowed && old.Denied.Contains("migrazione 14"), "Allineamento: database non aggiornato, motivo chiaro");
        Check(CreateJobDialog.FolderClient(new Customer("geco:1", "Rossi Srl", ShortName: "Rossi", Origin: Customer.GeCoOrigin)) == "Rossi" && CreateJobDialog.FolderClient(new Customer("x", "Bianchi Spa")) == "Bianchi Spa"
            && CreateJobDialog.MatchCustomers([new Customer("geco:1", "Rossi Srl", ShortName: "Ponti")], "ponti").Length == 1,
            "Creazione da modello: la parte «Cliente» si propone dal nome breve del cliente di GeCo; la ricerca trova anche il nome breve");
    }
}
