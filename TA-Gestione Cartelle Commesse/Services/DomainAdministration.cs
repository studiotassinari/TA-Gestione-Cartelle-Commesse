using System.Security.Principal;
namespace TA_Gestione_Cartelle_Commesse.Services;
public static class DomainAdministration
{
    // Controlla il token Windows effettivo, non il nome scritto nella finestra di accesso.
    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var domain = identity.User?.AccountDomainSid;
                return domain != null && new WindowsPrincipal(identity).IsInRole(new SecurityIdentifier(domain.Value + "-512"));
            }
            catch { return false; }
        }
    }
}
