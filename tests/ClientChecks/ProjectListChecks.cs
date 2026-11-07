using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TA_Gestione_Cartelle_Commesse;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Models;
using TA_Gestione_Cartelle_Commesse.ViewModels;
using TA_Gestione_Cartelle_Commesse.Views;
internal static partial class Program
{
    static void CheckProjectList(Application app)
    {
        var now=new DateTime(2026,11,7,10,0,0,DateTimeKind.Utc);
        var rows=Enumerable.Range(1,60).Select(i=>new FolderRecord($"C26{i:000}","26",i.ToString(),"Cliente rilevato","Progettazione strutturale e direzione lavori",$@"C:\test\C26{i:000}","Valido")
        {Inspection=new FolderInspection{LastFileWriteUtc=now.AddDays(-i),CheckedUtc=now,TotalBytes=i*1024,Complete=true}}).ToList();
        var category=new CategoryViewModel("Commesse",'C');
        category.ApplyState(new('C',@"C:\test",false,"","",new ArchiveSnapshot{Records=rows,SavedUtc=now}));
        Check(category.Projects.Count==50&&category.Projects[0].Code=="C26001"&&!category.Projects.Any(p=>p.Code=="C26060"),"Ultime 50 per attività, non per codice o data scansione");
        category.Filter="C26060"; Check(category.Projects.Count==1,"Ricerca anche fuori dalle prime 50");
        category.Filter="";
        var customer=new Customer("c1","Comune dimostrativo",Address:"Rimini");
        var models=rows.Select(r=>new WorkProject(r.Id,r.Code,r.Subject,CustomerId:"c1",Year:2026,Number:int.Parse(r.Number),Subject:r.Subject,LastActivityUtc:r.Inspection!.LastFileWriteUtc)).ToArray();
        models[59]=models[59] with{LastActivityUtc=now.AddHours(1)};
        category.ApplyProjectCatalog(new(models,[customer],true));
        Check(category.Projects[0].Code=="C26060"&&category.Projects[0].Client==customer.Name,"Attività gestionale e associazione cliente per ID");
        category.SelectedProject=category.Projects[0];var id=category.Selected!.Id;
        category.ApplyProjectCatalog(new(models,[customer with{Name="Comune aggiornato"}],true));
        Check(category.SelectedProject?.Id==id&&category.SelectedProject.Client=="Comune aggiornato","Selezione e rinomina cliente conservate");
        category.Filter="Comune aggiornato";Check(category.Projects.Count==50,"Ricerca per cliente associato");
        var directory=new CustomerDirectory();directory.Upsert(customer);
        using var project=new WorkProjectViewModel(models[0],directory);
        int changes=0;project.PropertyChanged+=(_,e)=>{if(e.PropertyName=="Client")changes++;};
        directory.Upsert(customer with{Name="Nuovo nome"});Check(project.Client=="Nuovo nome"&&changes>0,"Binding cliente condiviso aggiorna la riga");
        using var unattached=new WorkProjectViewModel(models[0] with{CustomerId="",SourceCustomerName=customer.Name},directory);
        Check(unattached.Customer==null,"Nessuna associazione cliente implicita dal nome");
        // Dati volutamente diversi: verificano che nessuna riga riusi il cliente o l'argomento delle altre.
        category.Filter="";
        var previewCustomers=new[]{
            new Customer("demo-a","Comune Alfa (esempio)"),
            new Customer("demo-b","Industria Beta (esempio)"),
            new Customer("demo-c","Condominio Gamma (esempio)")};
        string[] subjects=["Adeguamento sismico edificio scolastico","Ampliamento stabilimento produttivo",
            "Consolidamento delle fondazioni","Verifica di vulnerabilità del fabbricato","Ristrutturazione edificio residenziale"];
        var previewModels=models.Select((p,i)=>p with{
            CustomerId=i%4==3?"":previewCustomers[i%3].Id,
            SourceCustomerName=$"Committente da cartella {i+1:00}",
            Subject=subjects[i%subjects.Length]+$" · esempio {i+1:00}"
        }).ToArray();
        category.ApplyProjectCatalog(new(previewModels,previewCustomers,true));
        foreach(var row in category.Projects)
        {
            var expected=previewModels.Single(p=>p.Id==row.Id);
            Check(row.Subject==expected.Subject&&row.Client==(previewCustomers.FirstOrDefault(c=>c.Id==expected.CustomerId)?.Name??expected.SourceCustomerName+" · da associare"),
                row.Code+": cliente e argomento appartengono alla propria commessa");
        }
        var unaffected=category.Projects.First(p=>p.CustomerId=="demo-b");
        var unchangedId=unaffected.Id;var unchangedSubject=unaffected.Subject;
        category.ApplyProjectCatalog(new(previewModels,previewCustomers.Select(c=>c.Id=="demo-a"?c with{Name="Comune Alfa rinominato (esempio)"}:c).ToArray(),true));
        Check(category.Projects.Single(p=>p.Id==unchangedId).Client=="Industria Beta (esempio)"&&
            category.Projects.Single(p=>p.Id==unchangedId).Subject==unchangedSubject,
            "Rinominare un cliente non modifica commesse di altri clienti o argomenti");
        var main=new MainWindow();app.MainWindow=main;
        var view=new WorkProjectListView{DataContext=category,Resources=main.Resources,FontFamily=main.FontFamily,Background=main.Background};
        view.Measure(new Size(1560,680));view.Arrange(new Rect(0,0,1560,680));view.UpdateLayout();
        Directory.CreateDirectory("artifacts/checks");
        var image=new RenderTargetBitmap(1560,680,96,96,PixelFormats.Pbgra32);image.Render(view);
        using(var output=File.Create("artifacts/checks/elenco-commesse.png"))
        {var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));encoder.Save(output);}
        var details=new ProjectDetails(models[0],customer,[],[],[customer],[],true);
        using var business=new BusinessViewModel((cmd,ct)=>Task.FromResult(cmd.Action=="ProjectDetail"?JsonSerializer.SerializeToElement(details):JsonSerializer.SerializeToElement(new ProjectCatalog(models,[customer],true))));
        var detail=new ProjectDetailViewModel(business,models[0].Id);detail.Refresh().GetAwaiter().GetResult();
        detail.Project!.Subject="Bozza non salvata";detail.ReloadCustomers(customer).GetAwaiter().GetResult();
        Check(detail.Project.Subject=="Bozza non salvata"&&detail.Customer?.Id==customer.Id,"Gestione cliente conserva la bozza commessa");
        var customers=new CustomerManagementViewModel(business,customer.Id);customers.Refresh().GetAwaiter().GetResult();
        var customerWindow=new CustomerWindow(customers);
        var customerBody=(FrameworkElement)customerWindow.Content;
        customerBody.Measure(new Size(960,610));customerBody.Arrange(new Rect(0,0,960,610));customerBody.UpdateLayout();
        var customerImage=new RenderTargetBitmap(960,610,96,96,PixelFormats.Pbgra32);customerImage.Render(customerBody);
        using(var output=File.Create("artifacts/checks/anagrafica-clienti.png"))
        {var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(customerImage));encoder.Save(output);}
        Check(customers.CanSelect,"Cliente salvato selezionabile");customers.Editor!.Name="Bozza";
        Check(!customers.CanSelect,"Cliente non salvato non associabile");
    }
}

