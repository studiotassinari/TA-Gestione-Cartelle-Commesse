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
        return problems;
    }
    public static ConfigurationFile Load() => File.Exists(Path) ? Parse(File.ReadAllText(Path)) : new() { Domain = MachineDomain() };
    public static ConfigurationFile Parse(string json)
    {
        var server = JsonNode.Parse(json)?["Server"] as JsonObject ?? new JsonObject();
        string Text(string key) => server[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text.Trim() : "";
        string[] List(string key) => server[key] is JsonArray array ? array.Select(item => item is JsonValue value && value.TryGetValue<string>(out var text) ? text.Trim() : "").Where(item => item.Length > 0).ToArray() : [];
        int port = 5443;
        if (server["Port"] is JsonValue number && !number.TryGetValue(out port) && !(number.TryGetValue<string>(out var written) && int.TryParse(written, out port))) port = 5443;
        return new()
        {
            Domain = Text("Domain"), CollaboratorsGroup = Text("CollaboratorsGroup"), ManagementUsers = List("ManagementUsers"), Port = port,
            // Le configurazioni precedenti avevano un solo gruppo nella proprietà AllowedGroup.
            Groups = List("AllowedGroups").Append(Text("AllowedGroup")).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            JobsRoot = Text("JobsRoot"), BidsRoot = Text("BidsRoot"), ArchivedJobsRoot = Text("ArchivedJobsRoot"), ArchiveFolder = Text("ArchiveFolder"), JobTemplate = Text("JobTemplate")
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
        server["ArchiveFolder"] = ArchiveFolder; server["JobTemplate"] = JobTemplate;
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
