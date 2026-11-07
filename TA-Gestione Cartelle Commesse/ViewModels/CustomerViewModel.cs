using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;
public sealed class CustomerViewModel(Customer model)
{
    public Customer Model { get; } = model;
    public string Name => Model.Name;
    public string Address => Model.Address;
    public string TaxCode => Model.TaxCode;
    public string Vat => Model.Vat;
}

