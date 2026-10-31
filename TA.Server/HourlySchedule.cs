using System.Text.Json;
namespace TA.Server;

// Gli slot UTC sono distinti anche quando l'ora locale si ripete al cambio dell'ora legale.
public static class HourlySchedule
{
    public static DateTime Next(DateTime completedUtc) => new DateTime(completedUtc.Year, completedUtc.Month,
        completedUtc.Day, completedUtc.Hour, 0, 0, DateTimeKind.Utc).AddHours(1);
    public static bool IsDue(DateTime nowUtc, DateTime? completedUtc) => completedUtc == null || nowUtc >= Next(completedUtc.Value);
    public static DateTime? Load(string directory)
    {
        var path = Path.Combine(directory, "ultima-scansione.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<DateTime>(File.ReadAllText(path)) : null;
    }
    public static void Save(string directory, DateTime completedUtc)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "ultima-scansione.json"); var temp = target + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(completedUtc)); File.Move(temp, target, true);
    }
}
