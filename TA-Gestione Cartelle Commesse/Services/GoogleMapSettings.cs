using System.IO;
using System.Text.Json;
namespace TA_Gestione_Cartelle_Commesse.Services;

// La chiave browser resta fuori dal repository e dai pacchetti distribuiti.
public sealed record GoogleMapSettings(string ApiKey = "", string MapId = "")
{
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tassinari", "CommesseClient", "google-maps.json");
    public bool Configured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(MapId);
    public static GoogleMapSettings Load() => File.Exists(FilePath) ? JsonSerializer.Deserialize<GoogleMapSettings>(File.ReadAllText(FilePath)) ?? new() : new();
    public void Save()
    {
        if (ApiKey.Length > 256 || MapId.Length > 128 || ApiKey.Any(char.IsControl) || MapId.Any(char.IsControl)) throw new ArgumentException("Configurazione mappa non valida.");
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(this)); File.Move(temp, FilePath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
