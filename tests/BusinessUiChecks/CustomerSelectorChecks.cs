using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.ViewModels;
using TA_Gestione_Cartelle_Commesse.Views;
internal static class CustomerSelectorChecks
{
    public static void Run()
    {
        Customer[] clients=[new("a","Stesso nome",Vat:"001",Address:"Rimini"),new("b","Stesso nome",Vat:"002",Address:"Cesena")];
        var project=new WorkProject("p","C26183","Prova",CustomerId:"a",Subject:"Argomento originale");
        using var business=new BusinessViewModel((command,token)=>Task.FromResult(command.Action=="ProjectDetail"
            ?JsonSerializer.SerializeToElement(new ProjectDetails(project,clients[0],[],[],clients,[],true))
            :JsonSerializer.SerializeToElement(new ProjectCatalog([project],clients,true))));
        var vm=new ProjectDetailViewModel(business,"p");vm.Refresh().GetAwaiter().GetResult();
        var view=new WorkProjectView{DataContext=vm};Layout(view);
        var selector=Walk(view).OfType<CustomerSelectionView>().Single();
        var list=(ListBox)selector.FindName("CustomersList");
        if((list.SelectedItem as Customer)?.Id!="a")throw new Exception("Associazione iniziale non visualizzata");
        vm.Project!.Subject="Bozza argomento";vm.Project.Location="Bozza luogo";
        vm.CustomerSelection.Filter="002";Layout(view);
        if(list.Items.Count!=1||vm.Project.CustomerId!="a")throw new Exception("La ricerca ha alterato la selezione");
        list.SelectedItem=list.Items[0];Layout(view);
        if(vm.Project.CustomerId!="b"||vm.Customer?.Id!="b")throw new Exception("Scelta dalla lista non aggiorna il dettaglio");
        // Nuove istanze, ordine diverso e un cliente appena creato nel dialogo.
        var created=new Customer("c","Cliente creato",Vat:"003",Address:"Forlì",Revision:1);
        clients=[created,clients[1] with{Address="Cesena aggiornata"},clients[0] with{Address="Rimini aggiornata"}];
        vm.ReloadCustomers(created).GetAwaiter().GetResult();Layout(view);
        if((list.SelectedItem as Customer)?.Id!="c"||vm.Project.CustomerId!="c"||vm.Customer?.Id!="c"
            ||vm.CustomerSelection.Filter!="")throw new Exception($"Ritorno: lista={(list.SelectedItem as Customer)?.Id}; progetto={vm.Project.CustomerId}; cliente={vm.Customer?.Id}; filtro={vm.CustomerSelection.Filter}; stato={vm.Status}");
        if(vm.Project.Subject!="Bozza argomento"||vm.Project.Location!="Bozza luogo")throw new Exception("Ricarica clienti perde bozze");
        vm.CustomerSelection.Filter="nessuna corrispondenza";Layout(view);
        if(list.Items.Count!=0||vm.Project.CustomerId!="c")throw new Exception("Filtro vuoto cancella l'associazione");
        vm.CustomerSelection.Filter="003";Layout(view);
        if((list.SelectedItem as Customer)?.Id!="c")throw new Exception("Cliente selezionato non ripristinato al cambio filtro");
        vm.ReloadCustomers(null).GetAwaiter().GetResult();Layout(view);
        if(vm.Project.CustomerId!="c")throw new Exception("Chiusura dialogo senza scelta cambia il cliente");
        var management=new CustomerManagementViewModel(business,"c");management.Refresh().GetAwaiter().GetResult();
        var shared=new CustomerSelectionView{DataContext=management.CustomerSelection};Layout(shared);
        management.CustomerSelection.Filter="002";Layout(shared);
        var managedList=(ListBox)shared.FindName("CustomersList");managedList.SelectedItem=managedList.Items[0];Layout(shared);
        if(management.Selected?.Id!="b"||management.Editor?.Id!="b")throw new Exception("Selettore non condiviso con la gestione clienti");
        Console.WriteLine("CustomerSelectorChecks: filtri, omonimi, nuova anagrafica, ritorno dal dialogo e bozze verificati con controlli WPF.");
    }
    static void Layout(FrameworkElement view)
    {
        view.Measure(new Size(700,950));view.Arrange(new Rect(0,0,700,950));view.UpdateLayout();
        view.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    }
    static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var item in Walk(VisualTreeHelper.GetChild(root,i)))yield return item;
    }
}
