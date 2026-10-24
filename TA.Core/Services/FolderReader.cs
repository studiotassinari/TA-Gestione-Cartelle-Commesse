// Importa le operazioni su file, cartelle e flussi e le relative eccezioni.
using System.IO;
// Importa le espressioni regolari per interpretare anni e nomi delle cartelle.
using System.Text.RegularExpressions;
// Importa i dati delle cartelle e i risultati delle scansioni.
using TA_Gestione_Cartelle_Commesse.Models;

// Raggruppa questo tipo nel livello Services dellʼapplicazione.
namespace TA_Gestione_Cartelle_Commesse.Services;

// Implementa la lettura delle cartelle e la verifica delle convenzioni di denominazione.
public static class FolderReader
// Inizio del blocco di istruzioni o della definizione precedente.
{
    // Definisce cestini, snapshot e directory di sistema da escludere senza distinguere maiuscole e minuscole.
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { "@Recycle", "@Recently-Snapshot", "#recycle", "$RECYCLE.BIN", "System Volume Information", ".snapshot" };
    // Verifica l'esclusione confrontando soltanto l'ultimo componente del percorso.
    public static bool IsExcluded(string path) => Excluded.Contains(System.IO.Path.GetFileName(path));
    // Scansiona una radice per categoria e controlla periodicamente il token di annullamento.
    public static ScanResult Scan(string root, char prefix, CancellationToken token)
    // Inizio del blocco di istruzioni o della definizione precedente.
    {
        // Prepara gli elenchi separati delle cartelle trovate e delle segnalazioni.
        var records = new List<FolderRecord>(); var warnings = new List<string>();
        // Usa una pila per tenere traccia delle directory ancora da esaminare.
        var pending = new Stack<string>();
        // Distingue gli errori di enumerazione dalle semplici non conformità dei nomi.
        bool incomplete = false;
        // Restituisce subito un risultato annullato se la richiesta era già stata interrotta.
        if (token.IsCancellationRequested) return new([], [], true);
        // Applica alle sole commesse la selezione preliminare delle cartelle annuali.
        if (prefix == 'C')
        // Inizio del blocco di istruzioni o della definizione precedente.
        {
            // Gestisce gli errori durante l'accesso iniziale alla radice delle commesse.
            try
            // Inizio del blocco di istruzioni o della definizione precedente.
            {
                // Esamina esclusivamente le directory figlie dirette della radice per individuare gli anni.
                foreach (var yearFolder in Directory.EnumerateDirectories(root))
                // Inizio del blocco di istruzioni o della definizione precedente.
                {
                    // Consente l'interruzione anche durante la ricerca delle cartelle annuali.
                    if (token.IsCancellationRequested) return new([], [], true);
                    // Accetta solo nomi annuali di quattro cifre compresi tra 1900 e 2099.
                    if (!Regex.IsMatch(System.IO.Path.GetFileName(yearFolder), @"^(19|20)[0-9]{2}$")) continue;
                    // Aggiunge alla pila le cartelle annuali reali, senza seguire reparse point o collegamenti.
                    if ((File.GetAttributes(yearFolder) & FileAttributes.ReparsePoint) == 0) pending.Push(yearFolder);
                // Fine del blocco di istruzioni o della definizione corrente.
                }
                // Segnala quando la radice non contiene cartelle annuali utilizzabili.
                if (pending.Count == 0) warnings.Add("Nessuna cartella annuale principale trovata (nome atteso: quattro cifre, es. 2026).");
            // Fine del blocco di istruzioni o della definizione corrente.
            }
            // Restituisce un errore principale se la selezione delle cartelle annuali non riesce.
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new([], [], Error: ex.Message); }
        // Fine del blocco di istruzioni o della definizione corrente.
        }
        // Per le gare avvia la ricerca direttamente dalla radice configurata.
        else pending.Push(root);
        // Estrae codice, anno, progressivo, cliente e argomento dal formato C/GXXYYY - Cliente - Argomento.
        var pattern = new Regex(@"^(?<code>[CG](?<year>\d{2})(?<number>\d{3}))\s*-\s*(?<client>.+?)\s+-\s+(?<subject>.+)$", RegexOptions.IgnoreCase);
        // Preleva una directory alla volta fino a esaurire i percorsi da esaminare.
        while (pending.TryPop(out var current))
        // Inizio del blocco di istruzioni o della definizione precedente.
        {
            // Interrompe la scansione tra una directory e la successiva se richiesto.
            if (token.IsCancellationRequested) return new([], [], true);
            // Isola gli errori relativi all'enumerazione della directory corrente.
            try
            // Inizio del blocco di istruzioni o della definizione precedente.
            {
                // Enumera soltanto le cartelle immediatamente contenute nel percorso corrente.
                foreach (var directory in Directory.EnumerateDirectories(current))
                // Inizio del blocco di istruzioni o della definizione precedente.
                {
                    // Controlla l'annullamento prima di analizzare ciascuna cartella.
                    if (token.IsCancellationRequested) return new([], [], true);
                    // Salta cestini, snapshot e altre directory escluse per nome.
                    if (IsExcluded(directory)) continue;
                    // Isola gli errori relativi alla singola cartella senza interrompere le altre letture.
                    try
                    // Inizio del blocco di istruzioni o della definizione precedente.
                    {
                        // Segnala e ignora i reparse point, evitando di seguire collegamenti ad altri percorsi.
                        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { warnings.Add("Collegamento escluso: " + directory); continue; }
                        // Ricava il nome della cartella e tenta di interpretarlo con il formato atteso.
                        var name = System.IO.Path.GetFileName(directory); var match = pattern.Match(name);
                        // Considera valido soltanto un nome completo il cui prefisso corrisponde alla categoria richiesta.
                        if (match.Success && char.ToUpperInvariant(name[0]) == prefix)
                        // Inizio del blocco di istruzioni o della definizione precedente.
                        {
                            // Registra i campi estratti, normalizzando il codice e togliendo spazi esterni a cliente e argomento.
                            records.Add(new(match.Groups["code"].Value.ToUpperInvariant(), match.Groups["year"].Value, match.Groups["number"].Value, match.Groups["client"].Value.Trim(), match.Groups["subject"].Value.Trim(), directory, "Valido"));
                            // Passa alla cartella successiva senza esplorare l'interno di quella riconosciuta.
                            continue;
                        // Fine del blocco di istruzioni o della definizione corrente.
                        }
                        // Riconosce anche codici iniziali plausibili quando il resto del nome non rispetta il formato completo.
                        if (Regex.IsMatch(name, "^[CG][0-9]{5}", RegexOptions.IgnoreCase))
                        // Inizio del blocco di istruzioni o della definizione precedente.
                        {
                            // Esclude una cartella con codice appartenente all'altra categoria e aggiunge una segnalazione.
                            if (char.ToUpperInvariant(name[0]) != prefix) { warnings.Add("Codice di altra categoria escluso: " + directory); continue; }
                            // Conserva codice e anno riconoscibili, marcando il nome come non conforme senza dedurre il cliente.
                            records.Add(new(name[..6].ToUpperInvariant(), name.Substring(1, 2), name.Substring(3, 3), "Da verificare", name, directory, "Non conforme"));
                            // Registra l'anomalia e passa oltre senza entrare nella cartella.
                            warnings.Add("Nome non conforme: " + directory); continue;
                        // Fine del blocco di istruzioni o della definizione corrente.
                        }
                        // Per le commesse gestisce come anomalia anche una cartella priva di codice riconoscibile.
                        if (prefix == 'C')
                        // Inizio del blocco di istruzioni o della definizione precedente.
                        {
                            // Registra la cartella non conforme ricavando l'anno dalla directory annuale che la contiene.
                            records.Add(new("", System.IO.Path.GetFileName(current)[2..], "", "Da verificare", name, directory, "Non conforme"));
                            // Spiega nella segnalazione che il codice della commessa non è riconoscibile.
                            warnings.Add("Nome non conforme (codice commessa non riconosciuto): " + directory);
                        // Fine del blocco di istruzioni o della definizione corrente.
                        }
                        // Solo per le gare prosegue nei contenitori non riconosciuti; le commesse non vengono mai accodate qui.
                        else pending.Push(directory);
                    // Fine del blocco di istruzioni o della definizione corrente.
                    }
                    // Aggiunge l'errore della singola cartella alle segnalazioni e prosegue la scansione.
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { incomplete = true; warnings.Add(directory + ": " + ex.Message); }
                // Fine del blocco di istruzioni o della definizione corrente.
                }
            // Fine del blocco di istruzioni o della definizione corrente.
            }
            // Gestisce gli errori di enumerazione della directory corrente.
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            // Un errore sulla radice interrompe la lettura; un errore su un contenitore viene segnalato come lettura parziale.
            { if (current == root) return new([], [], Error: ex.Message); incomplete = true; warnings.Add(current + ": " + ex.Message); }
        // Fine del blocco di istruzioni o della definizione corrente.
        }
        // Restituisce le cartelle ordinate per anno e progressivo decrescenti, insieme alle segnalazioni.
        return new(records.OrderByDescending(r => r.Year).ThenByDescending(r => r.Number).ToList(), warnings, Incomplete: incomplete);
    // Fine del blocco di istruzioni o della definizione corrente.
    }
// Fine del blocco di istruzioni o della definizione corrente.
}
