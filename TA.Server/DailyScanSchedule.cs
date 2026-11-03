namespace TA.Server;

// Mezzanotte italiana, indipendentemente dal fuso della macchina e dall'ora legale.
public static class DailyScanSchedule
{
    public static DateTime Next(DateTime utc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        return TimeZoneInfo.ConvertTimeToUtc(local.Date.AddDays(1), zone);
    }
    public static bool IsDue(DateTime nowUtc, DateTime? completedUtc) => completedUtc == null || nowUtc >= Next(completedUtc.Value);
}
