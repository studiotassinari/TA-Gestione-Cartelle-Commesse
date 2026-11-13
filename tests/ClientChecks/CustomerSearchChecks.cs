using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Views;

internal static partial class Program
{
    // Anagrafiche sintetiche.
    static void CheckCustomerSearch()
    {
        var customers = new[] { new Customer("a", "Condominio Aurora 23"), new Customer("b", "Società Beta S.r.l.", Vat: "01234567890"), new Customer("c", "Comune di Esempio", TaxCode: "ABCDEF12G34H567I") };
        Check(CreateJobDialog.MatchCustomers(customers, "  ").Length == 3, "Ricerca committente vuota mostra tutto il catalogo");
        Check(CreateJobDialog.MatchCustomers(customers, "aurora cond").Single().Id == "a", "Ricerca committente per parole in qualsiasi ordine");
        Check(CreateJobDialog.MatchCustomers(customers, "societa").Single().Id == "b" && CreateJobDialog.MatchCustomers(customers, "0123456").Single().Id == "b", "Ricerca committente senza accenti e per partita IVA");
        Check(CreateJobDialog.MatchCustomers(customers, "abcdef12").Single().Id == "c" && CreateJobDialog.MatchCustomers(customers, "inesistente").Length == 0, "Ricerca committente per codice fiscale e senza risultati");
    }
}
