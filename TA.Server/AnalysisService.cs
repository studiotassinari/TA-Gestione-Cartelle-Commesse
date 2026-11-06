using System.Collections.Concurrent;
using System.Net;
using System.Text;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;

namespace TA.Server;

// Censimento giornaliero separato dalle risposte State, che restano leggere.
public sealed class AnalysisService(ServerSettings settings, ILogger<AnalysisService> logger)
{
    private readonly InventoryStore store = new(settings.InventoryFolder);
    public async Task RunAsync(IEnumerable<CategoryState> states, CancellationToken token, Action<ScanProgress>? reportProgress = null)
    {
        var roots = new[] { settings.JobsRoot, settings.BidsRoot, settings.ArchivedJobsRoot };
        var output = Path.GetFullPath(store.Root).TrimEnd('\\','/');
        if (roots.Any(r => output.Equals(Path.GetFullPath(r).TrimEnd('\\','/'), StringComparison.OrdinalIgnoreCase) ||
            output.StartsWith(Path.GetFullPath(r).TrimEnd('\\','/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("L'archivio JSON non può essere dentro una radice analizzata.");
        await Task.Run(() =>
        {
            var started = DateTime.UtcNow;
            reportProgress?.Invoke(new(true,"Preparazione inventario",0,null,"Censimento delle unità da analizzare",started));
            var snapshots=states.ToArray();
            var verified=snapshots.SelectMany(s=>(s.Snapshot?.Records??[]).Select(r=>(Category:s.Prefix.ToString(),Record:r))).GroupBy(x=>x.Category+"|"+x.Record.Path,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First().Record,StringComparer.OrdinalIgnoreCase);
            var plan = InventoryPlanner.Plan(snapshots.Select(s => (s.Prefix.ToString(), s.Root, s.Snapshot?.Records ?? [])), settings.JobsRoot, token);
            var folders = plan.Units.Where(u => !FileInventoryReader.IsArchive(u.Path)).Select(u => Path.GetFullPath(u.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var previous = store.Latest();
            var reports = new ConcurrentBag<InventoryManifest>();
            var progressGate = new object();
            var locations = new ConcurrentDictionary<string,ScanLocation>();
            int completed=0; bool warnings=plan.Issues.Count>0;
            Parallel.ForEach(plan.Units, new ParallelOptions { CancellationToken=token, MaxDegreeOfParallelism=Math.Clamp(settings.MaxConcurrentScans,1,4) }, unit =>
            {
                token.ThrowIfCancellationRequested();
                void Report(string path, string phase)
                {
                    locations[unit.Id]=new(unit.Category,phase,path);
                    lock(progressGate) reportProgress?.Invoke(new(true,phase,completed,plan.Units.Count,path,started,HasErrors:warnings,Locations:locations.Values.ToArray()));
                }
                Report(unit.Path,"Verifica metadati");
                try
                {
                    var excluded = new HashSet<string>(folders, StringComparer.OrdinalIgnoreCase); excluded.Remove(Path.GetFullPath(unit.Path));
                    var oldManifest = store.Find(unit.Id);
                    var inspection=verified.GetValueOrDefault(unit.Category+"|"+unit.Path)?.Inspection;
                    var signature=inspection?.Complete==true && !string.IsNullOrEmpty(inspection.ContentSignature) ? inspection.ContentSignature : null;
                    void SaveSignature() { if(signature!=null)store.SaveDocument("source-signature:"+unit.Id,signature); }
                    if(oldManifest?.Complete==true && oldManifest.Unit==unit && signature!=null && inspection!.CheckedUtc>=oldManifest.CheckedUtc && store.Document("source-signature:"+unit.Id)==System.Text.Json.JsonSerializer.Serialize(signature))
                    { reports.Add(store.Touch(oldManifest,token)); return; }
                    var probe = oldManifest?.Complete==true ? FileInventoryReader.Read(unit.Path, excluded, token, path=>Report(path,"Verifica metadati"), readArchives:false) : null;
                    if (oldManifest?.Complete == true && probe?.Complete == true && store.PhysicalMatches(oldManifest, probe))
                    { reports.Add(store.Touch(oldManifest, token)); SaveSignature(); return; }
                    var result = FileInventoryReader.Read(unit.Path, excluded, token,
                        path => Report(path,"Analisi approfondita"));
                    var manifest=store.Save(unit,result,token); reports.Add(manifest); if(result.Complete)SaveSignature();
                    lock(progressGate) warnings |= !manifest.Complete;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    lock(progressGate) { warnings=true; plan.Issues.Add(unit.Path + ": inventario non aggiornato: " + ex.Message); }
                    var old = previous?.Inventories.FirstOrDefault(m => m.Unit.Id == unit.Id);
                    if (old != null) reports.Add(old with { Complete = false, Added = 0, Removed = 0, Changed = 0, Baseline = false });
                }
                finally
                {
                    locations.TryRemove(unit.Id,out _);
                    lock(progressGate) reportProgress?.Invoke(new(true,"Inventario",++completed,plan.Units.Count,unit.Path,started,HasErrors:warnings,Locations:locations.Values.ToArray()));
                }
            });
            token.ThrowIfCancellationRequested();
            // Un'unità assente dal piano non dimostra da sola una cancellazione (rete o censimento incompleto).
            foreach (var old in previous?.Inventories ?? [])
                if (!reports.Any(m => m.Unit.Id == old.Unit.Id))
                {
                    bool absent=false;
                    if(plan.Issues.Count==0)
                    {
                        try
                        {
                            var source=roots.Where(r=>old.Unit.Path.StartsWith(r.TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)).OrderByDescending(r=>r.Length).FirstOrDefault();
                            if(source!=null)
                            {
                                _=Directory.GetFileSystemEntries(source);
                                try{_ = File.GetAttributes(old.Unit.Path);}
                                catch(FileNotFoundException){absent=true;}
                                catch(DirectoryNotFoundException){absent=true;}
                            }
                        }
                        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){ }
                    }
                    if(absent){store.RemoveConfirmed(old.Unit.Id);continue;}
                    plan.Issues.Add("Unità non rilevata nel censimento corrente, verificare rimozione o indisponibilità: " + old.Unit.Path);
                }
            reportProgress?.Invoke(new(true,"Salvataggio inventario",completed,plan.Units.Count,"Salvataggio JSON e differenze",started,HasErrors:warnings || plan.Issues.Count>0));
            store.SaveRun(new(started, DateTime.UtcNow, reports.ToList(), plan.Candidates, plan.Issues));
            logger.LogInformation("Censimento nomi completato: {Count} unità, {Candidates} proposte. Nessuna notifica.", reports.Count, plan.Candidates.Count);
        }, token);
    }
    public AnalysisPage Query(AnalysisQuery query)
    {
        if(query.Offset < 0 || query.Limit is < 1 or > 100 || query.Offset > int.MaxValue-query.Limit || query.Text.Length > 200)
            throw new ArgumentException("Paginazione o testo non valido.");
        var run = InventoryAvailability.Load(store) ?? throw new IOException("Inventario nomi non ancora disponibile: attendere le prime unità della scansione giornaliera oppure usare elenca_documenti per una cartella specifica.");
        IEnumerable<object> result;
        string note = "Nomi e metadati salvati, non contenuti. Proposte da verificare; nessuna classificazione automatica.";
        switch(query.Kind)
        {
            case "catalog":
                result = run.Inventories.Where(m => (m.Unit.Code + " " + m.Unit.Client + " " + m.Unit.Subject + " " + m.Unit.Path).Contains(query.Text, StringComparison.OrdinalIgnoreCase))
                    .Select(m => (object)new { m.Unit, m.CheckedUtc, m.Complete, m.EntryCount, m.Added, m.Removed, m.Changed, m.Baseline, issueCount = m.Issues.Count });
                break;
            case "candidates":
                result = run.Candidates.Where(c => (c.Client + " " + c.Subject).Contains(query.Text, StringComparison.OrdinalIgnoreCase)).Cast<object>();
                break;
            case "files":
                var manifest = run.Inventories.SingleOrDefault(m => m.Unit.Id == query.Id) ?? throw new ArgumentException("ID inventario non censito.");
                result = store.Entries(manifest).Where(e => (e.Container + " " + e.RelativePath).Contains(query.Text, StringComparison.OrdinalIgnoreCase)).Cast<object>();
                note += manifest.Complete ? "" : " Lettura parziale: alcuni nomi conservati provengono dalla scansione precedente.";
                break;
            case "issues":
                result = run.Issues.Concat(run.Inventories.SelectMany(m => m.Issues.Select(i => m.Unit.Path + ": " + i))).Cast<object>();
                break;
            case "patterns":
                var selected = run.Inventories.Where(m => m.Unit.Id == query.Id).ToArray();
                if (query.Id == null || selected.Length == 0) throw new ArgumentException("Selezionare un ID inventario per analizzare i pattern.");
                result = selected.SelectMany(m => store.Entries(m)).Where(e => e.Kind != "directory")
                    .GroupBy(e => Path.GetExtension(e.RelativePath).ToLowerInvariant())
                    .Select(g => new { extension = g.Key, count = g.Count(), examples = g.Select(e => Path.GetFileName(e.RelativePath)).Distinct().Take(5).ToArray() })
                    .OrderByDescending(g => g.count).Cast<object>();
                note += " Frequenze ed esempi per ragionare sui nomi; non interpretano il contenuto dei documenti.";
                break;
            default: throw new ArgumentException("Tipo analisi non valido.");
        }
        // Mantiene in memoria soltanto la pagina; le pagine JSON dei file sono lette una alla volta.
        int total = 0; var page = new List<object>();
        foreach(var item in result) { if(total >= query.Offset && page.Count < query.Limit) page.Add(item); total++; }
        return new(run.CompletedUtc, total, query.Offset + query.Limit < total ? query.Offset + query.Limit : null, page, note);
    }
    public string Report()
    {
        var run = store.Latest() ?? throw new IOException("Inventario nomi non ancora disponibile.");
        static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
        var text = new StringBuilder("<!doctype html><html lang='it'><meta charset='utf-8'><title>Analisi nomi</title><style>body{font:15px Segoe UI;margin:32px;color:#192c49}table{border-collapse:collapse;width:100%}td,th{padding:8px;border-bottom:1px solid #ddd;text-align:left}</style><h1>Analisi nomi e differenze</h1>");
        text.Append($"<p>Conclusa {run.CompletedUtc:O} (UTC). {run.Inventories.Count} unità; {run.Candidates.Count} proposte da verificare. Solo nomi e metadati, nessun contenuto. Le unità archivio possono sovrapporsi ai contenitori delle commesse.</p>");
        text.Append($"<p>Nuove voci: {run.Inventories.Sum(m => m.Added)}; rimosse confermate: {run.Inventories.Sum(m => m.Removed)}; metadati variati: {run.Inventories.Sum(m => m.Changed)}; prime letture: {run.Inventories.Count(m => m.Baseline)}; letture parziali: {run.Inventories.Count(m => !m.Complete)}. Nessuna notifica inviata.</p>");
        text.Append("<table><tr><th>Codice / unità</th><th>Committente</th><th>Voci</th><th>+ / − / variate</th><th>Esito</th></tr>");
        foreach(var m in run.Inventories.Where(m => m.Baseline || !m.Complete || m.Added+m.Removed+m.Changed > 0))
            text.Append($"<tr><td>{H(m.Unit.Code)} {H(m.Unit.Subject)}</td><td>{H(m.Unit.Client)}</td><td>{m.EntryCount}</td><td>{m.Added} / {m.Removed} / {m.Changed}</td><td>{(m.Baseline ? "Prima lettura; " : "")}{(m.Complete ? "Completa" : "Parziale; assenze non confermate")}</td></tr>");
        text.Append("</table><h2>Possibili commesse o argomenti</h2><ul>");
        foreach(var c in run.Candidates.Take(100)) text.Append($"<li>{H(c.Client)} — {H(c.Subject)}: {H(c.Evidence)} (confidenza {H(c.Confidence)})</li>");
        text.Append("</ul><p>Le altre proposte e i dettagli sono consultabili tramite MCP, con paginazione.</p><h2>Problemi di lettura</h2><ul>");
        foreach(var issue in run.Issues.Concat(run.Inventories.SelectMany(m => m.Issues.Select(i => m.Unit.Path + ": " + i))).Take(30)) text.Append("<li>" + H(issue) + "</li>");
        return text.Append("</ul></html>").ToString();
    }
}
