using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;
public sealed class WorkActivityViewModel(WorkActivity model)
{
    public WorkActivity Model { get; } = model;
    public string Title { get; set; } = model.Title;
    public string Assignee { get; set; } = model.Assignee;
    public DateTime Start { get; set; } = model.Start.ToDateTime(TimeOnly.MinValue);
    public DateTime End { get; set; } = model.End.ToDateTime(TimeOnly.MinValue);
    public string PlannedMinutes { get; set; } = model.PlannedMinutes.ToString();
    public string Progress { get; set; } = model.Progress.ToString();
    public string State { get; set; } = model.State;
    public WorkActivity ToModel()=>Model with{Title=Title,Assignee=Assignee,Start=DateOnly.FromDateTime(Start),End=DateOnly.FromDateTime(End),PlannedMinutes=Number(PlannedMinutes,"Minuti previsti"),Progress=Number(Progress,"Avanzamento"),State=State};
    private static int Number(string input,string label)=>int.TryParse(input,out var n)?n:throw new ArgumentException(label+": indicare un numero intero.");
}

