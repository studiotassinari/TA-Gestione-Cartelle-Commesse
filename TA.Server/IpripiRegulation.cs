using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server;

public static class IpripiRegulation
{
    private static readonly Lazy<IpripiRules> rules = new(() => {
        using var stream = typeof(IpripiRegulation).Assembly.GetManifestResourceStream("TA.Server.IpripiRules.json")
            ?? throw new InvalidDataException("Riferimento normativo IPRiPI mancante.");
        return JsonSerializer.Deserialize<IpripiRules>(stream) ?? throw new InvalidDataException("Riferimento normativo non valido.");
    });
    public static IpripiRules Get() => rules.Value;
    public static void Validate(IpripiIntervention item)
    {
        if (item.RuleCode.Length == 0)
        {
            if (item.Basis != "Da verificare" || item.Level.Length != 0) throw new ArgumentException("Un codice assente richiede stato Da verificare e livello vuoto.");
            return;
        }
        var rule = Get().Rules.SingleOrDefault(r => r.Code == item.RuleCode)
            ?? throw new ArgumentException("Codice non presente nell'Allegato 1 della DGR 2272/2016.");
        if (item.Level.Length > 0 && !rule.Levels.Contains(item.Level)) throw new ArgumentException("Livello non previsto per questo codice.");
        if (item.Basis == "Dedotto" && (item.Level.Length == 0 || item.MissingData.Count > 0))
            throw new ArgumentException("Con requisiti mancanti usare Da verificare, non Dedotto.");
    }
}
