using System.Collections.Concurrent;
using System.Security.Principal;
namespace TA.Transport;

// Il confronto usa il SID del token autenticato, mai nomi o ruoli inviati dal client.
public static class ManagementAccess
{
    public static readonly IReadOnlyList<string> Accounts = Array.AsReadOnly(new[]{"antonio.tassinari@ingtassinari.it","giuseppe.tassinari@ingtassinari.it"});
    private static readonly ConcurrentDictionary<string,string> Resolved = new();
    public static bool Allows(WindowsIdentity identity) => identity.IsAuthenticated && AllowsSid(identity.User?.Value, Resolve);
    public static bool AllowsSid(string? sid, Func<string,string?> resolve) => sid!=null && Accounts.Any(account=>sid==resolve(account));
    private static string? Resolve(string account)
    {
        try {return Resolved.GetOrAdd(account,a=>((SecurityIdentifier)new NTAccount(a).Translate(typeof(SecurityIdentifier))).Value);}
        catch(IdentityNotMappedException){return null;}
        catch(SystemException){return null;}
    }
}
