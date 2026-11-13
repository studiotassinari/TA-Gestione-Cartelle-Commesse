using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace TA.ServiceManager;

// Lettura e scrittura del file di configurazione senza perdere le proprietà che la finestra non mostra.
// Nessun dominio, gruppo o utente è scritto nel codice: i valori esistono solo in questo file del server.
public sealed class ConfigurationFile
{
    private static string Folder => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Tassinari", "CommesseServer");
    public static string Path => System.IO.Path.Combine(Folder, "server-settings.json");
    public string Domain { get; init; } = "";
    public string[] Groups { get; init; } = [];
    public string CollaboratorsGroup { get; init; } = "";
    public string[] ManagementUsers { get; init; } = [];
    public int Port { get; init; } = 5443;
    public string JobsRoot { get; init; } = "";
    public string BidsRoot { get; init; } = "";
    public string ArchivedJobsRoot { get; init; } = "";
    public string ArchiveFolder { get; init; } = "";
    public string JobTemplate { get; init; } = "";
    public string BidTemplate { get; init; } = "";
    public string CurriculumTemplate { get; init; } = "";
    public string CurriculumGraphics { get; init; } = "";
    // Analisi automatica locale e relativi limiti all'uso di risorse.
    public bool LocalAnalysis { get; init; }
    public int LocalAnalysisStartHour { get; init; } = 20;
    public int LocalAnalysisEndHour { get; init; } = 7;
    public int LocalAnalysisDocumentsPerHour { get; init; } = 30;
    // Incompleta finché mancano i gruppi: il servizio non si avvia e la finestra va proposta subito.
    public bool Complete => Groups.Length > 0 && Validate().Count == 0;

    public static string[] Split(string text) => text.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public List<string> Validate()
    {
        var problems = new List<string>();
        if (Groups.Length == 0) problems.Add("Indicare almeno un gruppo autorizzato.");
        if (Port is < 1024 or > 65535 or 8080 or 8443) problems.Add("Scegliere una porta TCP dedicata tra 1024 e 65535, diversa da 8080 e 8443.");
        foreach (var (label, path) in new[] { ("commesse", JobsRoot), ("gare", BidsRoot), ("archivio", ArchivedJobsRoot) })
            if (!path.StartsWith(@"\\", StringComparison.Ordinal)) problems.Add("La cartella " + label + " deve essere un percorso UNC (\\\\server\\condivisione\\...).");
        if (ArchiveFolder.Length == 0) problems.Add("Indicare la cartella dati del servizio.");
        if (CurriculumTemplate.Length > 0 && System.IO.Path.GetExtension(CurriculumTemplate).ToLowerInvariant() is not (".docx" or ".dotx")) problems.Add("Il modello Word del curriculum deve essere un file .docx o .dotx.");
        if (LocalAnalysisStartHour is < 0 or > 23 || LocalAnalysisEndHour is < 0 or > 23) problems.Add("Le ore dell'analisi automatica devono essere comprese tra 0 e 23.");
        if (LocalAnalysisDocumentsPerHour is < 1 or > 120) problems.Add("L'analisi automatica ammette da 1 a 120 documenti l'ora.");
        return problems;
    }
    public static ConfigurationFile Load() => File.Exists(Path) ? Parse(File.ReadAllText(Path)) : new() { Domain = MachineDomain() };
    public static ConfigurationFile Parse(string json)
    {
        var server = JsonNode.Parse(json)?["Server"] as JsonObject ?? new JsonObject();
        string Text(string key) => server[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text.Trim() : "";
        string[] List(string key) => server[key] is JsonArray array ? array.Select(item => item is JsonValue value && value.TryGetValue<string>(out var text) ? text.Trim() : "").Where(item => item.Length > 0).ToArray() : [];
        int Number(string key, int fallback)
        {
            if (server[key] is not JsonValue number) return fallback;
            if (number.TryGetValue<int>(out var value)) return value;
            return number.TryGetValue<string>(out var written) && int.TryParse(written, out var parsed) ? parsed : fallback;
        }
        return new()
        {
            Domain = Text("Domain"), CollaboratorsGroup = Text("CollaboratorsGroup"), ManagementUsers = List("ManagementUsers"), Port = Number("Port", 5443),
            LocalAnalysis = server["EnableLocalAnalysis"] is JsonValue flag && flag.TryGetValue<bool>(out var enabled) && enabled,
            LocalAnalysisStartHour = Number("LocalAnalysisStartHour", 20), LocalAnalysisEndHour = Number("LocalAnalysisEndHour", 7), LocalAnalysisDocumentsPerHour = Number("LocalAnalysisDocumentsPerHour", 30),
            // Le configurazioni precedenti avevano un solo gruppo nella proprietà AllowedGroup.
            Groups = List("AllowedGroups").Append(Text("AllowedGroup")).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            JobsRoot = Text("JobsRoot"), BidsRoot = Text("BidsRoot"), ArchivedJobsRoot = Text("ArchivedJobsRoot"), ArchiveFolder = Text("ArchiveFolder"), JobTemplate = Text("JobTemplate"), BidTemplate = Text("BidTemplate"), CurriculumTemplate = Text("CurriculumTemplate"), CurriculumGraphics = Text("CurriculumGraphics")
        };
    }
    // Aggiorna solo le proprietà gestite; tutto il resto del documento resta com'era.
    public string Merge(string? existing)
    {
        JsonObject root;
        try { root = (existing == null ? null : JsonNode.Parse(existing)) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { root = new JsonObject(); }
        if (root["Server"] is not JsonObject server) { server = new JsonObject(); root["Server"] = server; }
        server["Domain"] = Domain; server["AllowedGroups"] = new JsonArray(Groups.Select(g => (JsonNode?)JsonValue.Create(g)).ToArray());
        // Il primo gruppo resta anche nella proprietà storica, per poter tornare a una versione precedente del servizio.
        server["AllowedGroup"] = Groups.FirstOrDefault() ?? "";
        server["CollaboratorsGroup"] = CollaboratorsGroup;
        server["ManagementUsers"] = new JsonArray(ManagementUsers.Select(u => (JsonNode?)JsonValue.Create(u)).ToArray());
        server["Port"] = Port; server["JobsRoot"] = JobsRoot; server["BidsRoot"] = BidsRoot; server["ArchivedJobsRoot"] = ArchivedJobsRoot;
        server["ArchiveFolder"] = ArchiveFolder;
        // Campo vuoto = modello predefinito del servizio: una proprietà vuota nel file lo annullerebbe.
        if (JobTemplate.Length > 0) server["JobTemplate"] = JobTemplate; else server.Remove("JobTemplate");
        if (BidTemplate.Length > 0) server["BidTemplate"] = BidTemplate; else server.Remove("BidTemplate");
        if (CurriculumTemplate.Length > 0) server["CurriculumTemplate"] = CurriculumTemplate; else server.Remove("CurriculumTemplate");
        if (CurriculumGraphics.Length > 0) server["CurriculumGraphics"] = CurriculumGraphics; else server.Remove("CurriculumGraphics");
        server["EnableLocalAnalysis"] = LocalAnalysis; server["LocalAnalysisStartHour"] = LocalAnalysisStartHour;
        server["LocalAnalysisEndHour"] = LocalAnalysisEndHour; server["LocalAnalysisDocumentsPerHour"] = LocalAnalysisDocumentsPerHour;
        if (server["RefreshMinutes"] == null) server["RefreshMinutes"] = 60;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
    public void Save()
    {
        Directory.CreateDirectory(Folder);
        var merged = Merge(File.Exists(Path) ? File.ReadAllText(Path) : null);
        // Scrittura su file temporaneo e sostituzione: un'interruzione non lascia una configurazione troncata.
        var temporary = Path + ".tmp";
        File.WriteAllText(temporary, merged);
        File.Move(temporary, Path, true);
    }
    private static string MachineDomain()
    {
        try { return System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().DomainName; }
        catch (Exception) { return ""; }
    }
}
