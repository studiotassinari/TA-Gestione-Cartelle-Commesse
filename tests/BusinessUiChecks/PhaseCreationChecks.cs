using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.ViewModels;
internal static class PhaseCreationChecks
{
    public static void Run()
    {
        var project=new WorkProject("p","C24046","Prova",Year:2024,Number:46,Subject:"Prova");
        var details=new ProjectDetails(project,null,[],[],[],["utente"],true);
        using var business=new BusinessViewModel((command,token)=>{
            object response;
            switch(command.Action)
            {
                case "ProjectDetail":response=details;break;
                case "SavePhase":
                    var phase=command.Phase! with{Id="phase",Revision=1};
                    if(phase.ProjectId!="p"||phase.Name!="Progettazione")throw new Exception("Fase non compilata correttamente");
                    details=details with{Phases=[phase]};response=phase;break;
                case "SaveActivity":
                    var activity=command.Activity! with{Id="activity",Revision=1};
                    if(activity.PhaseId!="phase")throw new Exception("Attività non associata alla fase salvata");
                    details=details with{Activities=[activity]};response=activity;break;
                default:throw new Exception("Comando inatteso");
            }
            return Task.FromResult(JsonSerializer.SerializeToElement(response,response.GetType()));
        });
        var vm=new ProjectDetailViewModel(business,"p");vm.Refresh().GetAwaiter().GetResult();
        if(vm.CanEditPhase||vm.SavePhaseCommand.CanExecute(null))throw new Exception("Editor fase abilitato senza una fase");
        vm.NewPhaseCommand.Execute(null);
        if(vm.Phase==null||!vm.CanEditPhase||!vm.SavePhaseCommand.CanExecute(null))throw new Exception("Nuova fase non abilita il salvataggio");
        vm.Phase.Name="Progettazione";vm.SavePhaseCommand.Execute(null);
        if(vm.SelectedPhase?.Id!="phase"||vm.Details?.Phases.Length!=1||!vm.NewActivityCommand.CanExecute(null))throw new Exception("Prima fase non selezionata dopo salvataggio");
        vm.NewActivityCommand.Execute(null);
        if(vm.Activity==null||!vm.CanEditActivity||!vm.SaveActivityCommand.CanExecute(null))throw new Exception("Nuova attività non abilita il salvataggio");
        vm.Activity.Title="Elaborati";vm.Activity.Assignee="utente";vm.SaveActivityCommand.Execute(null);
        if(vm.Activities.Length!=1)throw new Exception("Attività salvata non visualizzata");
        details=details with{CanManage=false};vm.Refresh().GetAwaiter().GetResult();
        if(vm.NewPhaseCommand.CanExecute(null)||vm.SavePhaseCommand.CanExecute(null)||vm.CanEditPhase||vm.CanEditActivity)
            throw new Exception("Permessi di modifica non rispettati");
        Console.WriteLine("PhaseCreationChecks: prima fase, prima attività e autorizzazioni verificate.");
    }
}
