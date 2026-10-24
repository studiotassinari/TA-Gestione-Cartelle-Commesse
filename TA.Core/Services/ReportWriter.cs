// Importa le operazioni su file, cartelle e flussi e le relative eccezioni.
using System.IO;
// Importa i dati delle cartelle e i risultati delle scansioni.
using TA_Gestione_Cartelle_Commesse.Models;

// Raggruppa questo tipo nel livello Services dellʼapplicazione.
namespace TA_Gestione_Cartelle_Commesse.Services;

// Compone il report HTML autonomo con dati, segnalazioni e risorse grafiche incorporate.
public static class ReportWriter
// Inizio del blocco di istruzioni o della definizione precedente.
{
    // Legge una risorsa aziendale incorporata nell'applicazione e la converte per l'inclusione nel report.
    private static string BrandData(string name)
    // Inizio del blocco di istruzioni o della definizione precedente.
    {
        // Risolve il nome della risorsa tra le risorse incorporate nella libreria del server.
        var resource = typeof(ReportWriter).Assembly.GetManifestResourceStream("Brand." + name) ?? throw new IOException("Risorsa grafica assente: " + name);
        // Acquisisce il flusso della risorsa assicurandone la chiusura a fine metodo.
        using var stream = resource;
        // Prepara un buffer temporaneo in memoria, anch'esso rilasciato automaticamente.
        using var buffer = new MemoryStream();
        // Copia nel buffer i byte della risorsa incorporata.
        stream.CopyTo(buffer);
        // Restituisce i byte codificati Base64 per un URL dati HTML o CSS.
        return Convert.ToBase64String(buffer.ToArray());
    // Fine del blocco di istruzioni o della definizione corrente.
    }
    // Codifica i caratteri speciali HTML e sostituisce i valori nulli con testo vuoto.
    private static string H(string? value) => System.Net.WebUtility.HtmlEncode(value ?? "");
    // Genera un documento completo a partire da percorsi e risultati delle due categorie.
    public static string Create(string jobsRoot, ScanResult jobs, string bidsRoot, ScanResult bids, string? archivedRoot = null, ScanResult? archived = null)
    // Inizio del blocco di istruzioni o della definizione precedente.
    {
        // Accumula il contenuto HTML evitando concatenazioni ripetute dell'intero documento.
        var html = new System.Text.StringBuilder();
        // Apre il documento e definisce stile aziendale, tabelle, evidenziazione anomalie e stampa A4 orizzontale.
        html.Append("<!doctype html><html lang='it'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width'><title>TA - Report cartelle e non conformita</title><style>body{font:15px/1.5 Acherus,Segoe UI,Arial,sans-serif;color:#192845;background:#f3f6f6;margin:0}main{max-width:1400px;margin:auto;background:white;padding:40px}h1{font-size:30px;margin:0}h2{border-bottom:2px solid #135f6e;padding-bottom:8px;margin-top:36px}h3{margin-top:28px}p{max-width:1000px}.meta{color:#53677a}.summary{padding:18px;background:#edf5f5;border-left:5px solid #135f6e}.warning{background:#fff1d6}.path{font-size:12px;color:#526477;overflow-wrap:anywhere}table{border-collapse:collapse;width:100%;margin:14px 0 28px;table-layout:fixed}th,td{text-align:left;vertical-align:top;border:1px solid #d5dde6;padding:9px;overflow-wrap:anywhere}th{background:#135f6e;color:white}tr:nth-child(even){background:#f0f6f6}tr.bad{background:#fff1d6}a{color:#135f6e}thead{display:table-header-group}@media print{body{background:white}main{max-width:none;padding:0;font-size:10pt}tr{break-inside:avoid}h2,h3{break-after:avoid}@page{size:A4 landscape;margin:12mm}</style></head><body><main>");
        // Incorpora i font Acherus regolare e grassetto e definisce dimensioni del logo e colore del titolo.
        html.Append("<style>@font-face{font-family:Acherus;src:url(data:font/otf;base64," + BrandData("Acherus-Regular.otf") + ") format('opentype');font-weight:400}@font-face{font-family:Acherus;src:url(data:font/otf;base64," + BrandData("Acherus-Bold.otf") + ") format('opentype');font-weight:700}.brand{display:block;width:260px;height:auto;margin:44px}h1{color:#192845}</style>");
        // Incorpora il logo in Base64 affinché il report sia consultabile senza file grafici esterni.
        html.Append("<img class='brand' alt='Tassinari Associati' src='data:image/png;base64," + BrandData("Logo.png") + "'>");
        // Inserisce titolo e data/ora locale di generazione, codificando il testo per HTML.
        html.Append("<h1>Gestione Cartelle Commesse</h1><p class='meta'>Report delle cartelle e delle non conformita | " + H(DateTime.Now.ToString("dd/MM/yyyy HH:mm")) + "</p>");
        // Mostra i totali delle due categorie, dei nomi non validi e delle segnalazioni.
        html.Append("<div class='summary'><strong>" + jobs.Records.Count + " commesse · " + bids.Records.Count + " gare</strong><br>" + jobs.Records.Concat(bids.Records).Count(r => r.Status != "Valido") + " nomi non conformi. " + (jobs.Warnings.Count + bids.Warnings.Count) + " segnalazioni complessive.</div>");
        // Descrive limiti, formato atteso e ordine del report, aggiungendo i collegamenti alle due sezioni.
        html.Append("<p>Commesse: analisi delle sole cartelle di primo livello dentro ogni cartella annuale principale (1900–2099). I livelli successivi sono letti soltanto per dimensioni, date e ricerca dei preventivi, senza identificare ulteriori commesse. Gare: ricerca nella radice selezionata. Cestini, copie storiche e collegamenti esclusi. Nessuna cartella e stata modificata. Ordine: anno e codice crescenti, quindi cliente.</p><p>Formato atteso: <strong>CXXYYY - Cliente - Argomento</strong>, oppure <strong>GXXYYY - Cliente - Argomento</strong>. XX indica l'anno a due cifre; YYY il progressivo. Il controllo riguarda il formato del nome, non la correttezza amministrativa dei dati. Per i nomi non conformi, cliente e argomento non vengono dedotti.</p><nav><a href='#commesse'>Commesse</a> | <a href='#gare'>Gare</a></nav>");
        // Genera separatamente le sezioni delle commesse e delle gare.
        Section(html, "commesse", "Commesse", jobsRoot, jobs); Section(html, "gare", "Gare", bidsRoot, bids); if (archived != null) Section(html, "archivio", "Archivio ZIP/7z (sola lettura)", archivedRoot ?? "", archived);
        // Aggiunge le note sulle letture parziali e chiude il documento HTML.
        html.Append("<p class='meta'>Fine report. Le segnalazioni di accesso indicano possibili letture parziali. Le sottocartelle delle commesse e delle gare sono esplorabili nella gerarchia dell'applicazione, e non sono soggette al controllo di denominazione. Dimensioni e date includono i file dei livelli inferiori. Il controllo preventivi usa soltanto i nomi in amministraione, amministrazione e 99_AMMINISTRAZIONE: non certifica contenuto o approvazione.</p></main></body></html>");
        // Restituisce il documento completo come stringa da salvare.
        return html.ToString();
    // Fine del blocco di istruzioni o della definizione corrente.
    }
    // Genera titolo, segnalazioni e tabella delle cartelle per una singola categoria.
    private static void Section(System.Text.StringBuilder html, string id, string title, string root, ScanResult data)
    // Inizio del blocco di istruzioni o della definizione precedente.
    {
        // Inserisce l'ancora di navigazione, il titolo e il percorso radice con i testi codificati.
        html.Append("<h2 id='" + id + "'>" + H(title) + "</h2><p class='meta'>Radice: " + H(root) + "</p>");
        // Ordina il report per anno, codice, cliente e percorso crescenti, indipendentemente dall'ordine della vista.
        var ordered = data.Records.OrderBy(r => r.Year).ThenBy(r => r.Code).ThenBy(r => r.Client).ThenBy(r => r.Path).ToList();
        // Introduce l'elenco delle segnalazioni con il relativo conteggio.
        html.Append("<h3>Non conformita e segnalazioni (" + data.Warnings.Count + ")</h3>");
        // Esplicita l'assenza di segnalazioni invece di produrre una tabella vuota.
        if (data.Warnings.Count == 0) html.Append("<p>Nessuna segnalazione.</p>");
        // In presenza di segnalazioni crea una tabella numerata, ordinata alfabeticamente e con testi codificati.
        else { html.Append("<table><thead><tr><th style='width:50px'>N.</th><th>Segnalazione e cartella interessata</th></tr></thead><tbody>"); int n = 0; foreach (var warning in data.Warnings.OrderBy(w => w, StringComparer.CurrentCultureIgnoreCase)) html.Append("<tr class='bad'><td>" + (++n) + "</td><td>" + H(warning) + "</td></tr>"); html.Append("</tbody></table>"); }
        // Apre la tabella delle cartelle e definisce intestazioni e larghezze delle colonne.
        html.Append("<h3>Elenco ordinato delle cartelle (" + ordered.Count + ")</h3><table><thead><tr><th style='width:85px'>Codice</th><th style='width:45px'>Anno XX</th><th style='width:20%'>Cliente</th><th>Argomento e percorso</th><th style='width:100px'>Esito</th><th>Dimensione / ultima modifica file</th><th>Preventivo (nomi file)</th></tr></thead><tbody>");
        // Esamina ciascuna cartella nell'ordine di presentazione del report.
        foreach (var row in ordered)
            // Genera una riga con tutti i campi codificati e applica l'evidenziazione ai nomi non validi.
            html.Append("<tr" + (row.Status == "Valido" ? "" : " class='bad'") + "><td>" + H(row.Code) + "</td><td>" + H(row.Year) + "</td><td>" + H(row.Client) + "</td><td>" + H(row.Subject) + "<div class='path'>" + H(row.Path) + "</div></td><td>" + H(row.Status) + "</td><td>" + H(row.SizeText) + "<br>" + H(row.Inspection?.LastWriteText) + "</td><td>" + H(row.Inspection?.QuoteStatus) + "<div class='path'>" + H(row.Inspection?.Details) + "</div></td></tr>");
        // Chiude il corpo e la tabella delle cartelle della sezione corrente.
        html.Append("</tbody></table>");
    // Fine del blocco di istruzioni o della definizione corrente.
    }
// Fine del blocco di istruzioni o della definizione corrente.
}
