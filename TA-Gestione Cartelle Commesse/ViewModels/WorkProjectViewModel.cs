using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;
public sealed class WorkProjectViewModel(WorkProject model)
{
    public WorkProject Model { get; } = model;
    public string Code => Model.Code;
    public int Year => Model.Year;
    public int Number => Model.Number;
    public string SourceCustomerName => Model.SourceCustomerName;
    public string CustomerId { get; set; } = model.CustomerId;
    public string Subject { get; set; } = model.Subject;
    public string Location { get; set; } = model.Location;
    public string Status { get; set; } = model.Status;
    public WorkProject ToModel()=>Model with{CustomerId=CustomerId,Subject=Subject,Location=Location,Status=Status};
}

