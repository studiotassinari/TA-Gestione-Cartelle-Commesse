using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;
public sealed class WorkPhaseViewModel(WorkPhase model)
{
    public WorkPhase Model { get; } = model;
    public string Name { get; set; } = model.Name;
    public string Description { get; set; } = model.Description;
    public string Order { get; set; } = model.Order.ToString();
    public WorkPhase ToModel()=>Model with{Name=Name,Description=Description,Order=int.TryParse(Order,out var n)&&n>=0?n:throw new ArgumentException("L'ordine della fase deve essere un numero intero positivo o zero.")};
}

