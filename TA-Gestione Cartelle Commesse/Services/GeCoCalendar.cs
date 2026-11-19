using System.Globalization;
using System.IO;
using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA_Gestione_Cartelle_Commesse.Services;

// Canale verso il servizio per pianificazione e ore di GeCo: le finestre non conoscono il trasporto, così si provano con dati sintetici.
public interface IGeCoGateway { Task<T> Send<T>(GeCoCommand command, CancellationToken token = default); }
public sealed class ServerGeCoGateway(TA.Transport.ServerConnection server) : IGeCoGateway
{
    public Task<T> Send<T>(GeCoCommand command, CancellationToken token = default) => server.SendAsync<T>(new("GeCo", GeCo: command), token);
}
// Canale di una finestra: ogni comando porta l'identificativo della finestra (Scheda), che il servizio scrive negli eventi
// tecnici dei salvataggi; quando l'evento torna indietro, la finestra che ha salvato lo riconosce e non si ricarica.
public sealed class WindowGeCoGateway(IGeCoGateway inner) : IGeCoGateway
{
    public string Window { get; } = "geca-" + Guid.NewGuid().ToString("N")[..12];
    public Task<T> Send<T>(GeCoCommand command, CancellationToken token = default) => inner.Send<T>(command.Scheda == null ? command with { Scheda = Window } : command, token);
}

// Calcoli di sola presentazione comuni alle due finestre, come nelle pagine di GeCo2027: le date viaggiano come testo
// aaaa-mm-gg, la pianificazione conta i soli giorni feriali dal lunedì iniziale. Le regole restano sul servizio.
public static class GeCoCalendar
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");
    public static readonly string[] DayNames = ["Lun", "Mar", "Mer", "Gio", "Ven", "Sab", "Dom"];
    // Colori delle commesse (sfondo, testo), gli stessi della Pianificazione di GeCo2027.
    public static readonly (string Back, string Fore)[] Palette = [("#135F6E", "#FFFFFF"), ("#192845", "#FFFFFF"), ("#E3C77A", "#192845"), ("#BFE0E6", "#192845"), ("#8A4B14", "#FFFFFF"), ("#D9DEE2", "#192845")];
    // Il colore dipende solo dall'id della commessa, non dalla posizione nell'elenco: aggiungerne una non cambia il colore
    // delle altre. Stessa regola di GeCo2027 (components/ColoriCommesse.js), così la commessa ha lo stesso colore ovunque.
    public static (string Back, string Fore) ProjectColor(int project) => Palette[((project % Palette.Length) + Palette.Length) % Palette.Length];

    public static DateTime Date(string iso) => DateTime.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string Iso(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string AddDays(string iso, int days) => Iso(Date(iso).AddDays(days));
    public static DateTime Monday(DateTime day) => day.Date.AddDays(-(((int)day.DayOfWeek + 6) % 7));
    public static string Short(string iso) => Date(iso).ToString("d MMM", Italian);
    public static string Long(string iso) => Date(iso).ToString("dddd d MMMM", Italian);
    public static int IsoWeek(string iso) => ISOWeek.GetWeekOfYear(Date(iso));

    // Indice lavorativo di una data rispetto al lunedì iniziale; sabato e domenica cadono sul venerdì (fine) o sul lunedì successivo (inizio).
    public static int WorkIndex(string monday, string iso, bool end)
    {
        int days = (int)(Date(iso) - Date(monday)).TotalDays;
        int week = (int)Math.Floor(days / 7.0), dow = days - week * 7;
        return dow <= 4 ? week * 5 + dow : end ? week * 5 + 4 : (week + 1) * 5;
    }
    public static string WorkDate(string monday, int index)
    {
        int week = (int)Math.Floor(index / 5.0);
        return AddDays(monday, week * 7 + (index - week * 5));
    }

    // Righe parallele: ogni elemento va nella prima riga libera nei suoi giorni (Start incluso, Start+Length escluso).
    public static Dictionary<TKey, int> Rows<TKey>(IEnumerable<(TKey Key, int Start, int Length)> items, out int count) where TKey : notnull
    {
        var row = new Dictionary<TKey, int>(); var ends = new List<int>();
        foreach (var item in items.OrderBy(i => i.Start))
        {
            int r = 0; while (r < ends.Count && ends[r] > item.Start) r++;
            if (r == ends.Count) ends.Add(0);
            ends[r] = item.Start + item.Length; row[item.Key] = r;
        }
        count = ends.Count; return row;
    }

    // Valore scritto nella casella, come lo legge la pagina di GeCo2027 per i totali: 7,5 - 7.5 - 7:30; il servizio lo ricontrolla al salvataggio.
    public static decimal Hours(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return 0;
        if (t.Contains(':'))
        {
            var parts = t.Split(':');
            return (int.TryParse(parts[0], out var h) ? h : 0) + (parts.Length > 1 && int.TryParse(parts[1], out var m) ? m / 60m : 0);
        }
        return decimal.TryParse(t.Replace('.', ','), NumberStyles.Number, Italian, out var v) ? v : 0;
    }
    public static string Format(decimal? value) => value is decimal v && v != 0 ? v.ToString("0.##", Italian) : "";
    public static string Total(decimal value) => value.ToString("0.##", Italian);
    // Solo cifre e separatori, come la casella della pagina web.
    public static string Clean(string text) => new(text.Where(c => char.IsDigit(c) || c is '.' or ',' or ':').ToArray());
}

// Persone scelte nel diagramma, ricordate sul PC dell'utente come nel browser per GeCo2027: solo una comodità, senza permessi.
public static class GeCoChoices
{
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tassinari", "CommesseClient", "geco-persone.json");
    public static int[]? Load(string page)
    {
        try { return File.Exists(FilePath) && JsonSerializer.Deserialize<Dictionary<string, int[]?>>(File.ReadAllText(FilePath)) is { } all && all.TryGetValue(page, out var ids) ? ids : null; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
    public static void Save(string page, int[]? ids)
    {
        try
        {
            var all = File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, int[]?>>(File.ReadAllText(FilePath)) ?? new() : new();
            if (ids == null) all.Remove(page); else all[page] = ids;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.WriteAllText(FilePath, JsonSerializer.Serialize(all));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { ClientLog.Write("Scelta delle persone non ricordata: " + ex.Message); }
    }
}
