using System.Globalization;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA_Gestione_Cartelle_Commesse.Services;

// Collegamento con il server sostituibile nelle verifiche automatiche.
public interface ICurriculumGateway { Task<T> Send<T>(CurriculumCommand command); }
public sealed class ServerCurriculumGateway(TA.Transport.ServerConnection server) : ICurriculumGateway
{
    public Task<T> Send<T>(CurriculumCommand command) => server.SendAsync<T>(new("Curriculum", Curriculum: command));
}

// Conversioni tra i campi di testo della scheda e i dati del lavoro, e ricerca nel curriculum.
public static class CurriculumText
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");
    public static string Plain(string? value) => string.Concat((value ?? "").Normalize(System.Text.NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToUpperInvariant();
    // Ogni parola scritta deve comparire in almeno un campo: titolo, committente, luogo, descrizione, prestazioni, parole chiave, categorie, codici collegati o anni.
    public static CurriculumWork[] Match(IEnumerable<CurriculumWork> works, string text)
    {
        var words = Plain(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return works.ToArray();
        return works.Where(w =>
        {
            var target = Plain(string.Join(" ", new[] { w.Title, w.Customer, w.Place, w.Description, w.Services, w.Role, w.Reference, Period(w) }
                .Concat(w.Keywords ?? []).Concat(w.ProjectCodes ?? []).Concat((w.Categories ?? []).Select(c => c.Code + " " + c.Description))
                .Concat((w.Photos ?? []).Select(p => p.Caption))));
            return words.All(word => target.Contains(word));
        }).ToArray();
    }
    public static string Period(CurriculumWork w) => (w.Start, w.End) switch
    {
        ({ } start, { } end) => start.Year == end.Year ? start.Year.ToString() : start.Year + "–" + end.Year,
        ({ } start, null) => "dal " + start.Year,
        (null, { } end) => "fino al " + end.Year,
        _ => ""
    };
    public static string[] List(string text) => text.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public static string Amount(decimal? value) => value?.ToString("N2", Italian) ?? "";
    // Vuoto = importo non indicato; un testo non numerico è un errore da segnalare, non uno zero.
    public static decimal? Amount(string text, string label)
    {
        text = text.Replace("€", "").Trim();
        if (text.Length == 0) return null;
        return decimal.TryParse(text, NumberStyles.Number, Italian, out var value) && value >= 0 ? value : throw new FormatException(label + ": importo non valido (esempio 250.000,00).");
    }
    // Una categoria per riga: «codice; importo; descrizione», per esempio «S.03; 250.000,00; Strutture in cemento armato».
    public static string Categories(CurriculumCategory[]? categories) =>
        string.Join(Environment.NewLine, (categories ?? []).Select(c => c.Code + "; " + Amount(c.Amount) + (c.Description.Length > 0 ? "; " + c.Description : "")));
    public static CurriculumCategory[] Categories(string text) => text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line =>
    {
        var parts = line.Split(';', 3, StringSplitOptions.TrimEntries);
        if (parts[0].Length == 0) throw new FormatException("Categoria senza codice: " + line);
        return new CurriculumCategory(parts[0], parts.Length > 1 ? Amount(parts[1], "Categoria " + parts[0]) ?? 0 : 0, parts.Length > 2 ? parts[2] : "");
    }).ToArray();
}
