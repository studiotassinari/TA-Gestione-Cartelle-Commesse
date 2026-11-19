using GeCo.Dati.Regole;
using TA.Server.SharedDatabase;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server.Business;

// Collegamento confermato da un responsabile autorizzato tra un cliente dell'archivio locale (Id) e un cliente di GeCo: vale per
// le proposte e i casi ambigui, che le regole non decidono da sole. GeCoId 0 non si usa: togliere il collegamento lo elimina.
public sealed record CustomerLink(string Id, int GeCoId, string Actor, DateTime Utc, int Revision = 0);

// Catalogo dei clienti dalla 2.40.0: con il database comune configurato è quello di GeCo (anagrafica unica, letta in sola
// lettura) e l'archivio locale resta soltanto come storico. Un cliente locale con la stessa P.IVA o lo stesso codice fiscale di
// un cliente di GeCo (Abbinato, regole IdentitaClienti) o collegato a mano diventa quel cliente: le commesse che lo citano
// mostrano il cliente di GeCo. Gli altri restano nel catalogo come clienti locali, finché un responsabile autorizzato non li
// collega o li importa in GeCo dal modulo Allineamento. Senza database comune tutto resta come prima.
public sealed partial class BusinessService
{
    public GeCoCustomerSource? GeCoCustomers { get; init; }
    public sealed record CustomerView(Customer[] All, IReadOnlyDictionary<string, string> Alias, bool FromGeCo, string? Problem);
    private readonly object customerGate = new();
    private (GeCoCustomerSource.Catalog Catalog, long Change, CustomerView View)? customerCache;

    public CustomerView CustomerCatalog()
    {
        var local = Store.All<Customer>("customer");
        if (GeCoCustomers?.Current() is not { } geco) return new(local, new Dictionary<string, string>(), false, GeCoCustomers?.LastError);
        var change = Store.ChangeNumber;
        lock (customerGate)
        {
            if (customerCache is { } c && ReferenceEquals(c.Catalog, geco) && c.Change == change) return c.View;
            var ids = geco.Clienti.Select(x => x.Id).ToHashSet();
            var links = LinksById();
            var matches = ServizioAllineamento.AbbinaClientiEsterni(local.Select(GeCoCustomerSource.ToExternal).ToList(), geco.Clienti);
            var alias = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var m in matches)
            {
                int? to = links.TryGetValue(m.Cliente.Id, out var linked) && ids.Contains(linked) ? linked : m.Esito == EsitoClienteEsterno.Abbinato ? m.IdCliente : null;
                if (to is int id) alias[m.Cliente.Id] = GeCoCustomerSource.Id(id);
            }
            var all = geco.Clienti.Select(x => GeCoCustomerSource.ToCustomer(x, geco.ReadUtc)).Concat(local.Where(x => !alias.ContainsKey(x.Id))).ToArray();
            var view = new CustomerView(all, alias, true, GeCoCustomers.LastError);
            customerCache = (geco, change, view);
            return view;
        }
    }
    public Customer[] Customers() => CustomerCatalog().All;
    // Cliente citato da una commessa: di GeCo, locale ritrovato in GeCo (quello di GeCo) o locale dello storico.
    public Customer? FindCustomer(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var view = CustomerCatalog();
        if (view.Alias.TryGetValue(id, out var to)) id = to;
        return view.All.FirstOrDefault(c => c.Id == id) ?? (view.FromGeCo ? null : Store.Find<Customer>("customer", id));
    }
    // Con l'anagrafica di GeCo i clienti locali non si scrivono più: un secondo archivio tornerebbe a divergere da GeCo.
    private void LocalCustomersWritable()
    {
        if (GeCoCustomers?.Current() != null)
            throw new UnauthorizedAccessException("I clienti sono quelli di GeCo: inserirli o correggerli in GeCo2027 (pagina Clienti) oppure importare in GeCo quelli di GeCa da Gestione > Allineamento. L'archivio locale dei clienti resta consultabile come storico.");
    }

    private Dictionary<string, int> LinksById() => Store.All<CustomerLink>("customer-link").Where(l => l.GeCoId > 0).ToDictionary(l => l.Id, l => l.GeCoId, StringComparer.Ordinal);

    // ---------------------------------------------------------------- per il modulo Allineamento (permessi decisi dal chiamante)

    public Customer[] LocalCustomers() => Store.All<Customer>("customer");
    public IReadOnlyDictionary<string, int> CustomerLinks() => LinksById();
    public IReadOnlyDictionary<string, int> CustomerProjects() => Store.All<WorkProject>("project").Where(p => !string.IsNullOrEmpty(p.CustomerId))
        .GroupBy(p => p.CustomerId!).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
    public void LinkCustomer(string localId, int? geCoId, string actor) => Store.Locked(() =>
    {
        if (Store.Find<Customer>("customer", localId ?? "") == null) throw new KeyNotFoundException("Cliente locale non trovato (forse già collegato o eliminato).");
        var old = Store.Find<CustomerLink>("customer-link", localId!);
        if (geCoId is not int id || id <= 0)
        {
            if (old != null) Store.Delete("customer-link", localId!, old.Revision, actor);
            return true;
        }
        Store.Save("customer-link", localId!, old?.Revision ?? 0, new CustomerLink(localId!, id, actor, DateTime.UtcNow, (old?.Revision ?? 0) + 1), actor, "CustomerLink");
        return true;
    });
    public void CustomersChanged() => GeCoCustomers?.Invalidate();
}
