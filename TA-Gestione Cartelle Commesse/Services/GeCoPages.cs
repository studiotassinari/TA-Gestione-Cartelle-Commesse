using System.Diagnostics;
namespace TA_Gestione_Cartelle_Commesse.Services;

// Pagine di GeCo2027 che GeCa non porta nel programma (2.42.0): fatture, SAL, analisi, WIP, tabelle, stampe, raccordo degli
// utenti Windows. GeCa apre la pagina nel browser, già sulla commessa quando la pagina lo prevede (parametro IdComm, lo stesso
// dei collegamenti tra le pagine di GeCo2027), all'indirizzo di GeCo configurato sul server (Gestore server > Configura).
// Accesso, permessi e importi li decide GeCo2027 come sempre: qui si compone soltanto l'indirizzo.
public sealed record GeCoPage(string Testo, string Pagina, bool Commessa, string Spiegazione);

public static class GeCoPages
{
    // Pagine della commessa: si aprono sulla commessa (Commessa_dettaglio.aspx?IdComm=…).
    public static readonly GeCoPage[] OfProject =
    [
        new("Dettaglio della commessa", "Commessa_dettaglio.aspx", true, "La scheda completa della commessa in GeCo2027, con tutte le schede"),
        new("Importi e budget", "Commessa_ValoriEconomici.aspx", true, "Valori economici e budget dei costi della commessa"),
        new("Rilevazioni", "Commessa_Rilevazioni.aspx", true, "Rilevazioni (ore e costi) registrate sulla commessa"),
        new("Fatture", "Fatture.aspx", true, "Fatture della commessa"),
        new("Richieste di fattura", "RichiesteFattura.aspx", true, "Richieste di fattura della commessa"),
        new("Piano di fatturazione attivo", "Commessa_PianoFattAttivo.aspx", true, "Piano di fatturazione attivo della commessa"),
        new("Incassi", "ScadenzarioIncassi.aspx", true, "Scadenzario degli incassi della commessa"),
        new("Lettere di incarico", "LettereIncarico.aspx", true, "Lettere di incarico della commessa"),
        new("Classificazioni", "Commessa_Parametri.aspx", true, "Classificazioni della commessa"),
        new("Documenti", "Commessa_Allegati.aspx", true, "Documenti allegati alla commessa"),
        new("Commesse collegate", "Commessa_CommColl.aspx", true, "Commesse collegate: aggiunta e rimozione"),
    ];

    // Pagine generali (menu Gestione > Apri in GeCo2027).
    public static readonly GeCoPage[] General =
    [
        new("Gestione commesse", "commesse.aspx", false, "Ricerca, inserimento, duplicazione ed esportazione delle commesse"),
        new("Fatture", "Fatture.aspx", false, "Fatture emesse e da emettere"),
        new("Richieste di fattura", "RichiesteFattura.aspx", false, "Richieste di fattura"),
        new("SAL", "Sal.aspx", false, "Stati di avanzamento dei lavori"),
        new("Analisi economica delle commesse", "AnalisiEconomicaComm.aspx", false, "Analisi economica (costi e ricavi) delle commesse"),
        new("Calcolo WIP", "CalcoloWip.aspx", false, "Calcolo dei lavori in corso"),
        new("Scadenzario incassi", "ScadenzarioIncassi.aspx", false, "Scadenzario degli incassi"),
        new("Clienti", "Clienti.aspx", false, "Anagrafica dei clienti"),
        new("Raccordo utenti Windows", "RaccordoWindows.aspx", false, "Creazione di utenti di GeCo dagli account Windows (amministratori di GeCo)"),
        new("Richieste di accesso (GeCo2027)", "RichiesteAccesso.aspx", false, "La stessa pagina delle richieste di accesso, nel browser"),
    ];

    // Indirizzo completo della pagina; null se l'indirizzo di GeCo non è configurato o non è un indirizzo http/https.
    public static string? Url(string? address, GeCoPage page, int idCommessa = 0)
    {
        var a = (address ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(a, UriKind.Absolute, out var root) || root.Scheme is not ("http" or "https")) return null;
        return a + "/" + page.Pagina + (page.Commessa && idCommessa > 0 ? "?IdComm=" + idCommessa : "");
    }

    public const string NotConfigured = "L'indirizzo di GeCo2027 non è configurato sul server: un responsabile autorizzato lo indica in Gestore server > Configura (Indirizzo di GeCo).";

    // Apre l'indirizzo con il browser predefinito; restituisce il messaggio da mostrare se non si può.
    public static string? Open(string? address, GeCoPage page, int idCommessa = 0)
    {
        if (Url(address, page, idCommessa) is not { } url) return NotConfigured;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); return null; }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return "Impossibile aprire il browser: " + ex.Message; }
    }
}
