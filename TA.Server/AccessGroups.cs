using System.Security.Principal;
namespace TA.Server;

// Gruppi e dominio provengono solo dalla configurazione dell'installazione: nessun nome è scritto nel codice.
public static class AccessGroups
{
    // Un nome non risolvibile non blocca gli altri gruppi: viene restituito a parte per il registro.
    public static (SecurityIdentifier[] Sids, string[] Unresolved) Resolve(IEnumerable<string> names)
    {
        var sids = new List<SecurityIdentifier>(); var missing = new List<string>();
        foreach (var name in names)
        {
            try { sids.Add((SecurityIdentifier)new NTAccount(name).Translate(typeof(SecurityIdentifier))); }
            catch (IdentityNotMappedException) { missing.Add(name); }
        }
        return (sids.ToArray(), missing.ToArray());
    }
    // SID del dominio configurato; in sua assenza quello del primo gruppo autorizzato di dominio.
    // null significa «non determinabile»: chi chiama decide come trattare il caso.
    public static SecurityIdentifier? DomainSid(ServerSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Domain))
        {
            try
            {
                var sid = (SecurityIdentifier)new NTAccount(settings.Domain.Trim()).Translate(typeof(SecurityIdentifier));
                if ((sid.AccountDomainSid ?? sid) is { } domain) return domain;
            }
            catch (IdentityNotMappedException) { }
            catch (SystemException) { }
        }
        return Resolve(settings.AuthorizedGroups()).Sids.Select(s => s.AccountDomainSid).FirstOrDefault(s => s != null);
    }
}
