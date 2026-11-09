using System.Globalization;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA.Server;

public static class BackupRetentionPolicy
{
    // Cinque giorni di calendario italiani, incluso oggi; configurazioni precedenti non superano il limite.
    public static string[] Obsolete(IEnumerable<string> files,int configured,DateTime nowUtc,bool inventory)
    {
        var days=Math.Clamp(configured,1,5);
        var cutoff=DailyReports.ItalianTime(nowUtc).Date.AddDays(1-days);
        var dated=files.Select(path=>
        {
            var name=Path.GetFileNameWithoutExtension(path);
            var stamp=name[(inventory?"TA-Commesse-".Length:"gestionale-".Length)..];
            var valid=DateTime.TryParseExact(stamp,inventory?"yyyyMMdd'T'HHmmssfffffff":"yyyyMMdd",CultureInfo.InvariantCulture,
                inventory?DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal:DateTimeStyles.None,out var date);
            return (Path:path,Valid:valid,Day:inventory&&valid?DailyReports.ItalianTime(date).Date:date.Date);
        }).Where(x=>x.Valid).OrderByDescending(x=>Path.GetFileName(x.Path),StringComparer.Ordinal).ToArray();
        return dated.Where((x,index)=>x.Day<cutoff||index>=days).Select(x=>x.Path).ToArray();
    }
}
