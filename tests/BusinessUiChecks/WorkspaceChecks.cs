using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.ViewModels;
using TA_Gestione_Cartelle_Commesse.Views;
internal static class WorkspaceChecks
{
    public static void Run()
    {
        Customer[] customers=[new("a","Cliente A"),new("b","Cliente B")];
        WorkProject[] projects=[new("p1","C26005","Primo",Number:5,CustomerId:"a"),new("p2","C26012","Secondo",Number:12,CustomerId:"b")];
        var business=new BusinessViewModel((command,token)=>Task.FromResult(command.Action=="ProjectDetail"
            ?JsonSerializer.SerializeToElement(new ProjectDetails(projects.Single(p=>p.Id==command.Id),null,[],[],customers,[],true))
            :JsonSerializer.SerializeToElement(new ProjectCatalog(projects,customers,true))));
        var owner=new Window{Width=1,Height=1,Opacity=0,ShowInTaskbar=false};owner.Show();
        using(var workspace=new EntityWorkspace(business,owner))
        {
            workspace.ShowProject("p1");Pump();var first=workspace.ProjectWindow!;
            var model=(ProjectDetailViewModel)first.DataContext;
            Check(model.Project!.NumberText=="005","Numero sempre a tre cifre");
            workspace.ShowProject("p2");Pump();
            Check(ReferenceEquals(first,workspace.ProjectWindow)&&((ProjectDetailViewModel)first.DataContext).Id=="p2","Cambio contenuto nella stessa finestra");
            model=(ProjectDetailViewModel)first.DataContext;model.Project!.Subject="Bozza";
            workspace.ShowProject("p1");Pump();
            Check(ReferenceEquals(first.DataContext,model)&&model.Project.Subject=="Bozza","Navigazione non perde modifiche non salvate");
            model.Refresh().GetAwaiter().GetResult();
            model.ManageCustomerCommand.Execute(null);Pump();var client=workspace.CustomerWindow!;
            var chooser=(CustomerManagementViewModel)client.DataContext;chooser.SelectCustomer("a");Pump();
            var associate=Walk(client).OfType<Button>().Single(b=>b.Content?.ToString()?.StartsWith("Associa cliente a ")==true);
            associate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
            Check(model.Project!.CustomerId=="a","Scelta cliente non modale aggiorna la commessa di origine");
            model.Refresh().GetAwaiter().GetResult();
            workspace.ShowCustomers("a");Pump();
            Check(ReferenceEquals(client,workspace.CustomerWindow)&&((CustomerManagementViewModel)client.DataContext).Selected?.Id=="a","Stessa finestra clienti da tutti gli accessi");
            var clientModel=(CustomerManagementViewModel)client.DataContext;clientModel.Editor!.Name="Bozza cliente";
            workspace.ShowCustomers("b");
            Check(clientModel.Selected?.Id=="a"&&clientModel.Editor.Name=="Bozza cliente","Cambio cliente protegge la bozza");
            clientModel.Refresh().GetAwaiter().GetResult();clientModel.SelectedProject=clientModel.AssociatedProjects.Single();
            clientModel.OpenAssociatedProjectCommand.Execute(null);Pump();
            Check(ReferenceEquals(first,workspace.ProjectWindow)&&((ProjectDetailViewModel)first.DataContext).Id=="p1","Da cliente apre stessa finestra commesse");
            Check(first.Owner==owner&&client.Owner==owner,"Finestre paritarie con proprietario principale");
        }
        owner.Close();
    }
    private static void Pump()
    {
        var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);
    }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Walk(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    private static void Check(bool value,string text){if(!value)throw new Exception(text);Console.WriteLine("PASS: "+text);}
}
