using System.Diagnostics;
using System.Globalization;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;

namespace TA.Server;

// Cerca solo nei JSON completati: nessun accesso ai file o agli archivi originali durante la richiesta.
public sealed class FileSearchService(ServerSettings settings)
{
    private readonly InventoryStore store = new(settings.ArchiveFolder);
    public FileSearchPage Search(FileSearchQuery query, CancellationToken token = default)
    {
        FileSearchValidation.Validate(query);
        var run = store.Latest() ?? throw new IOException("Inventario dei singoli file non ancora disponibile: attendere la scansione delle 23:00. Non equivale a zero risultati.");
        var version = run.CompletedUtc.ToString("O", CultureInfo.InvariantCulture);
        if (query.Version != null && query.Version != version)
            throw new ArgumentException("Inventario cambiato: ripartire da offset 0 senza version.");
        var extension = query.Extension.Length == 0 ? "" : "." + query.Extension.TrimStart('.');
        var selected = run.Inventories.Where(m =>
            (query.Category.Length == 0 || m.Unit.Category == query.Category) &&
            (query.Year.Length == 0 || m.Unit.Year == query.Year) &&
            (query.Code.Length == 0 || m.Unit.Code.StartsWith(query.Code, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(m => string.IsNullOrEmpty(m.Unit.Code)).ThenBy(m => m.Unit.Category, StringComparer.Ordinal)
            .ThenBy(m => m.Unit.Path, StringComparer.Ordinal).ToArray();
        var files = new List<FileSearchItem>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        int total = 0; bool complete = run.Issues.Count == 0 && selected.All(m => m.Complete);
        var warnings = run.Issues.Take(10).ToList();
        if (!complete) warnings.Add("Inventario parziale: totale dei nomi censiti, alcuni dati possono provenire dalla verifica precedente. Non dimostra assenza di altri file.");
        var clock = Stopwatch.StartNew();
        foreach (var manifest in selected)
        {
            foreach(var entry in store.Entries(manifest))
            {
                token.ThrowIfCancellationRequested();
                if (clock.Elapsed > TimeSpan.FromSeconds(20))
                    throw new IOException("Ricerca troppo ampia per ottenere un totale completo: restringere anno o codice commessa. Nessun totale parziale restituito.");
                if(entry.Kind == "directory") continue;
                var name = entry.RelativePath.Replace('\\','/').Split('/').Last();
                if (!name.Contains(query.Name, StringComparison.OrdinalIgnoreCase) ||
                    (extension.Length > 0 && !Path.GetExtension(name).Equals(extension, StringComparison.OrdinalIgnoreCase))) continue;
                var location = entry.Location ?? FileLocator.Locate(manifest.Unit, entry);
                if (!seen.Add(location.Id)) continue; // Stesso archivio presente in C e A: una sola occorrenza fisica.
                if(total >= query.Offset && files.Count < query.Limit)
                    files.Add(new(manifest.Unit.Code, manifest.Unit.Category, manifest.Unit.Year, manifest.Unit.Id,
                        entry.RelativePath, entry.Container, location, entry.Bytes, entry.ModifiedUtc, manifest.CheckedUtc, manifest.Complete));
                total++;
            }
        }
        warnings.Add("Solo nomi e metadati. La data della scansione riguarda i JSON, non un controllo in tempo reale. Gli archivi annidati non vengono decompressi.");
        return new(version, run.CompletedUtc, run.StartedUtc, total, query.Offset,
            query.Offset + query.Limit < total ? query.Offset + query.Limit : null, complete, files, warnings);
    }
}
