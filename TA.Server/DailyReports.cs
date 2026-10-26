using System.Net;
using System.Text;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA.Server;

// Il calendario usa l'ora italiana, indipendentemente dal fuso della VM.
public static class DailyReports
{
    public static DateTime ItalianTime(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time")).DateTime;
    public static bool IsDue(DateTimeOffset utc, string directory)
    {
        var local = ItalianTime(utc);
        return local.Hour >= 23 && !File.Exists(Path.Combine(directory, local.ToString("yyyy-MM-dd") + ".html"));
    }
    public static string Latest(string directory)
    {
        var file = Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "????-??-??.html").OrderDescending().FirstOrDefault() : null;
        return file == null ? throw new IOException("Nessun report giornaliero disponibile. La prima elaborazione parte alle 23:00, ora italiana.") : File.ReadAllText(file);
    }
    public static void Save(string directory, DateTime date, IEnumerable<CategoryWorker> workers, DateTimeOffset started)
    {
        string H(string? value) => WebUtility.HtmlEncode(value) ?? "";
        var states = workers.Select(w => w.State).ToArray();
        var jobs = states.Single(s => s.Prefix == 'C').Snapshot;
        var bids = states.Single(s => s.Prefix == 'G').Snapshot;
        var archived = states.Single(s => s.Prefix == 'A').Snapshot;
        var html = ReportWriter.Create(jobs?.Root ?? "", new(jobs?.Records ?? [], jobs?.Warnings ?? []), bids?.Root ?? "", new(bids?.Records ?? [], bids?.Warnings ?? []), archived?.Root ?? "", new(archived?.Records ?? [], archived?.Warnings ?? []));
        var detail = new StringBuilder($"<section><h2>Report giornaliero {date:dd/MM/yyyy}</h2><p>Avvio {ItalianTime(started):dd/MM/yyyy HH:mm:ss}; fine {ItalianTime(DateTimeOffset.UtcNow):dd/MM/yyyy HH:mm:ss} (ora italiana). I dati rappresentano le letture effettuate durante questa elaborazione, non una fotografia atomica.</p>");
        foreach (var state in states)
        {
            detail.Append($"<h3>Categoria {state.Prefix}</h3><p>{H(state.Message)} — {H(state.ArchiveStatus)}</p>");
            foreach (var record in state.Snapshot?.Records ?? [])
            {
                var inspection = record.Inspection;
                bool fresh = inspection != null && inspection.CheckedUtc >= started.UtcDateTime;
                detail.Append($"<details><summary>{H(record.Code)} — {H(record.Client)} — {H(record.SizeText)}</summary><p>{(fresh ? "Verificato in questa elaborazione" : "NON verificato in questa elaborazione: dati precedenti o assenti")}; ultima verifica UTC: {H(inspection?.CheckedUtc.ToString("O"))}</p><pre>");
                foreach (var path in inspection?.ArchiveDirectories ?? []) detail.AppendLine(H(path));
                detail.Append($"</pre><p>{H(inspection?.Details)}</p></details>");
            }
        }
        detail.Append("</section>");
        html = html.Replace("<main>", "<main>" + detail);
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, date.ToString("yyyy-MM-dd") + ".html");
        var temp = target + ".tmp";
        File.WriteAllText(temp, html, Encoding.UTF8);
        File.Move(temp, target, true);
    }
}
