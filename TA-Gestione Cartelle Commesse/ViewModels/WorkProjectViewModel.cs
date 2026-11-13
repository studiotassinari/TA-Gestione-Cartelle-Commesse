using System.ComponentModel;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
using TA_Gestione_Cartelle_Commesse.Models;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;
public sealed class WorkProjectViewModel : Observable,IDisposable
{
    private readonly CustomerDirectory customers;
    public WorkProjectViewModel(WorkProject model,CustomerDirectory? customers=null,FolderRecord? folder=null)
    {
        Model=model;this.customers=customers??new();Folder=folder;
        customerId=model.CustomerId;subject=model.Subject;location=model.Location;status=model.Status;BindCustomer();
        ArchiveFolderCommand=new(()=>ArchiveRequested?.Invoke(this),()=>Folder!=null && Model.Category is "C" or "G" && Services.DomainAdministration.IsEnabled);
        EditCommand=new(()=>EditRequested?.Invoke(this));
        OpenCustomerCommand=new(()=>CustomerRequested?.Invoke(this),()=>Customer!=null);
        OpenFolderCommand=new(()=>FolderRequested?.Invoke(this),()=>Folder!=null);
    }
    public event Action<WorkProjectViewModel>? EditRequested,CustomerRequested,FolderRequested,ArchiveRequested;
    public Command ArchiveFolderCommand {get;}
    public Command EditCommand {get;}
    public Command OpenCustomerCommand {get;}
    public Command OpenFolderCommand {get;}
    public WorkProject Model {get;}
    public FolderRecord? Folder {get;}
    public string Id=>Model.Id;
    public string Code=>Model.Code;
    public int Year=>Model.Year;
    public int Number=>Model.Number;
    public string NumberText=>Number.ToString("D3");
    public string SourceCustomerName=>Model.SourceCustomerName;
    private string customerId,subject,location,status;
    public CustomerViewModel? Customer {get;private set;}
    public string CustomerId {get=>customerId;set{customerId=value;BindCustomer();Notify();}}
    public string Subject {get=>subject;set{subject=value;Notify();}}
    public string Location {get=>location;set{location=value;Notify();}}
    public string Status {get=>status;set{status=value;Notify();Notify(nameof(StateLabel));}}
    public string Client=>Customer?.Name??(SourceCustomerName.Length>0?SourceCustomerName+" · da associare":"Cliente da associare");
    public string StateLabel=>Status switch{"Open"=>"Aperta","Closed"=>"Chiusa","Invoiced"=>"Fatturata",_=>"Stato non impostato"};
    public string LastActivityText=>Model.LastActivityUtc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm")??"Data non ancora disponibile";
    public string SizeText=>Folder?.SizeText??"";
    // Preventivo: esito dai nomi dei file, mostrato con un colore nell'elenco.
    public int QuoteLevel=>QuoteIndicator.Level(Folder?.Inspection?.QuoteStatus);
    public string QuoteLabel=>QuoteIndicator.Label(Folder?.Inspection?.QuoteStatus);
    public string QuoteDetail
    {
        get
        {
            var inspection=Folder?.Inspection;
            if(inspection==null)return "Preventivo non ancora verificato: in attesa della scansione.";
            var files=inspection.QuoteFiles.Count>0?Environment.NewLine+string.Join(Environment.NewLine,inspection.QuoteFiles.Take(10)):"";
            return inspection.QuoteStatus+" — controllo basato solo sui nomi dei file nella cartella amministrazione, da verificare."+files;
        }
    }
    public WorkProject ToModel()=>Model with{CustomerId=CustomerId,Subject=Subject,Location=Location,Status=Status};
    public void UpdateCustomer(Customer value){customers.Upsert(value);BindCustomer();}
    private void BindCustomer()
    {
        if(Customer!=null)Customer.PropertyChanged-=CustomerChanged;
        Customer=customers.Find(customerId);if(Customer!=null)Customer.PropertyChanged+=CustomerChanged;
        Notify(nameof(Customer));Notify(nameof(Client));OpenCustomerCommand?.Refresh();
    }
    private void CustomerChanged(object? sender,PropertyChangedEventArgs e)=>Notify(nameof(Client));
    public void Dispose(){if(Customer!=null)Customer.PropertyChanged-=CustomerChanged;}
}
