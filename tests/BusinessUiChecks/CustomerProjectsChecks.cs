using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.ViewModels;
internal static class CustomerProjectsChecks
{
    public static void Run()
    {
        Customer[] clients=[new("a","Cliente omonimo"),new("b","Cliente omonimo"),new("c","Senza commesse")];
        var projects=Enumerable.Range(1,60).Select(i=>new WorkProject("p"+i,$"C26{i:000}","Progetto "+i,
            CustomerId:"a",Year:2026,Number:i,Subject:"Argomento "+i)).Concat([
            new WorkProject("other","C25001","Altro cliente",CustomerId:"b"),
            new WorkProject("unlinked","C24001","Da associare",SourceCustomerName:"Cliente omonimo"),
            new WorkProject("tender","G26001","Gara",CustomerId:"a",Category:"G"),
            new WorkProject("archive","C23001","Archivio",CustomerId:"a",Category:"A")
        ]).ToArray();
        using var business=new BusinessViewModel((command,token)=>Task.FromResult(command.Action=="ProjectCatalog"
            ?JsonSerializer.SerializeToElement(new ProjectCatalog(projects,clients,true))
            :JsonSerializer.SerializeToElement(command.Project!)));
        using var vm=new CustomerManagementViewModel(business,"a");vm.Refresh().GetAwaiter().GetResult();
        if(vm.AssociatedProjects.Length!=60||vm.AssociatedProjects[0].Number!=60)throw new Exception("Elenco cliente incompleto o non ordinato");
        vm.Selected=clients[1];
        if(vm.AssociatedProjects.Single().Id!="other")throw new Exception("Clienti omonimi confusi");
        vm.Selected=clients[2];
        if(vm.AssociatedProjects.Length!=0||!vm.AssociatedSummary.StartsWith("Nessuna"))throw new Exception("Stato vuoto non mostrato");
        vm.Selected=clients[0];vm.SelectedProject=vm.AssociatedProjects[0];
        string? opened=null;vm.ProjectRequested+=project=>opened=project.Id;vm.OpenAssociatedProjectCommand.Execute(null);
        if(opened!=vm.SelectedProject.Id)throw new Exception("Apertura dettaglio errata");
        business.Send<JsonElement>(new("SaveProjectDetails",Project:projects[0] with{CustomerId="b",Revision=1})).GetAwaiter().GetResult();
        if(vm.AssociatedProjects.Length!=59)throw new Exception("Cambio cliente non aggiornato immediatamente");
        vm.Selected=clients[1];
        if(vm.AssociatedProjects.Length!=2)throw new Exception("Nuova associazione non mostrata");
        vm.NewCommand.Execute(null);
        if(vm.AssociatedProjects.Length!=0||vm.OpenAssociatedProjectCommand.CanExecute(null))throw new Exception("Nuovo cliente conserva commesse precedenti");
        Console.WriteLine("CustomerProjectsChecks: sole associazioni per ID, omonimi, elenco completo, cambio cliente e apertura dettaglio verificati.");
    }
}
