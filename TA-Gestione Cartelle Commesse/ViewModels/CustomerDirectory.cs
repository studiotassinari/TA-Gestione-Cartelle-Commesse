using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;
public sealed class CustomerDirectory
{
    private readonly Dictionary<string,CustomerViewModel> customers=new();
    public CustomerViewModel? Find(string id)=>customers.GetValueOrDefault(id);
    public IEnumerable<CustomerViewModel> Search(string query)=>customers.Values.Where(c=>c.Matches(query)).OrderBy(c=>c.Name);
    public void Upsert(Customer customer){if(customers.TryGetValue(customer.Id,out var vm))vm.Load(customer);else customers[customer.Id]=new(customer);}
    public void Replace(IEnumerable<Customer> values)
    {
        var rows=values.ToArray();foreach(var row in rows)Upsert(row);
        var ids=rows.Select(c=>c.Id).ToHashSet();foreach(var key in customers.Keys.Where(k=>!ids.Contains(k)).ToArray())customers.Remove(key);
    }
}

