using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;
public sealed class CustomerViewModel : Observable
{
    public CustomerViewModel(Customer model){Model=model;Load(model);}
    public Customer Model {get;private set;}
    private string name="",vat="",tax="",address="",email="";
    public string Id=>Model.Id;
    public string Name {get=>name;set{name=value;Notify();}}
    public string Vat {get=>vat;set{vat=value;Notify();}}
    public string TaxCode {get=>tax;set{tax=value;Notify();}}
    public string Address {get=>address;set{address=value;Notify();}}
    public string Email {get=>email;set{email=value;Notify();}}
    public Customer ToModel()=>Model with{Name=Name,Vat=Vat,TaxCode=TaxCode,Address=Address,Email=Email};
    public bool Matches(string query)=>Model.Matches(query);
    public void Load(Customer model){Model=model;Name=model.Name;Vat=model.Vat;TaxCode=model.TaxCode;Address=model.Address;Email=model.Email;Notify(nameof(Id));}
}
