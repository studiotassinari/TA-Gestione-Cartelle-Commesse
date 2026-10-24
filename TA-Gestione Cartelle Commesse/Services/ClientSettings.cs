using System.IO;
using System.Text.Json;
namespace TA_Gestione_Cartelle_Commesse.Services;

// Contiene solo destinazione e porta; le credenziali provengono dalla sessione Windows corrente.
public sealed record ClientSettings(string Host = "", int Port = 5443)
{
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tassinari", "CommesseClient", "connection.json");
    public static ClientSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new();
        return JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(SettingsPath)) ?? new();
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this));
    }
}
