namespace TA_Gestione_Cartelle_Commesse.Services;
// Collegamenti alle pagine di GeCo. L'indirizzo arriva dal server: si accetta solo http o https, così un valore
// sbagliato in configurazione non può far avviare un programma o aprire un file dal client.
public static class GeCoLink
{
    public static string? Planning(string? address) => Page(address, "Pianificazione.aspx");
    public static string? Page(string? address, string page)
    {
        var text = (address ?? "").Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var site) || site.Scheme is not ("http" or "https") || site.Host.Length == 0) return null;
        return site.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/" + page;
    }
}
