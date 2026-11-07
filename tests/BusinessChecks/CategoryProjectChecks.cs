using TA.Server.Business;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Models;
internal static class CategoryProjectChecks
{
    public static void Run(string folder)
    {
        var source=new[]{
            ProjectMetadata.FromFolder(new FolderRecord("C26046","26","46","Cliente C","Progetto attivo",@"C:\test\lavori\2026\C26046","Valido"),"C"),
            ProjectMetadata.FromFolder(new FolderRecord("G26046","26","46","Cliente G","Gara lavori",@"C:\test\gare\2026\G26046","Valido"),"G"),
            ProjectMetadata.FromFolder(new FolderRecord("C26046","26","46","Cliente A","Progetto storico",@"C:\test\archivio\2026\C26046.zip","Valido"),"A")
        };
        var service=new BusinessService(new BusinessStore(folder),()=>source,s=>s,()=>["lettore"]);
        var customer=(Customer)service.Execute(new("SaveCustomer",Customer:new("","Cliente associato")),"gestore",true);
        foreach(var category in new[]{"C","G","A"})
        {
            var catalog=(ProjectCatalog)service.Execute(new("ProjectCatalog",Category:category),"lettore",false);
            if(catalog.Projects.Length!=1||catalog.Projects[0].Category!=category)throw new Exception("Categorie mescolate");
            var project=catalog.Projects[0];
            if(project.Year!=2026||project.Number!=46)throw new Exception("Anno/numero non riconosciuti");
            var saved=(WorkProject)service.Execute(new("SaveProjectDetails",Project:project with{CustomerId=customer.Id,Subject="Aggiornato "+category,Category="C"}),"gestore",true);
            if(saved.Category!=category)throw new Exception("Categoria alterata dal client");
            var phase=(WorkPhase)service.Execute(new("SavePhase",Phase:new("",project.Id,"Fase iniziale")),"gestore",true);
            var detail=(ProjectDetails)service.Execute(new("ProjectDetail",Id:project.Id),"lettore",false);
            if(detail.Customer?.Id!=customer.Id||detail.Project.Subject!="Aggiornato "+category||detail.Phases.Single().Id!=phase.Id||detail.CanManage)
                throw new Exception("Dettaglio categoria incompleto");
        }
        if(service.Projects().Select(p=>p.Id).Distinct().Count()!=3)throw new Exception("Codice uguale confonde commessa e archivio");
        Console.WriteLine("CategoryProjectChecks: C/G/A separate, anno/numero, clienti e fasi salvati nel solo database.");
    }
}
