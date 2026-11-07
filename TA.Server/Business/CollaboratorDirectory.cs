using System.DirectoryServices.AccountManagement;
using System.Security.Principal;
namespace TA.Server.Business;

// Lettura AD con l'identità del servizio; nessuna password o anagrafica locale duplicata.
public sealed class CollaboratorDirectory(string group)
{
    private readonly object gate = new();
    private string[] users = [];
    private DateTime expires;
    public string[] Members()
    {
        lock (gate)
        {
            if (DateTime.UtcNow < expires) return users.ToArray();
            using var context = new PrincipalContext(ContextType.Domain, "ingtassinari.it");
            using var principal = GroupPrincipal.FindByIdentity(context, IdentityType.SamAccountName, group)
                ?? throw new IOException($"Gruppo collaboratori {group} non trovato in Active Directory.");
            using var members = principal.GetMembers(true);
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in members)
            {
                using (member)
                    if (member is UserPrincipal { Enabled: true, Sid: not null } user)
                        result.Add(((NTAccount)user.Sid.Translate(typeof(NTAccount))).Value);
            }
            users = result.Order(StringComparer.OrdinalIgnoreCase).ToArray();
            expires = DateTime.UtcNow.AddMinutes(1);
            return users.ToArray();
        }
    }
}
