using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace TA_Gestione_Cartelle_Commesse.Views;
public sealed class WorkProjectView : UserControl
{
    public WorkProjectView()
    {
        var panel=new StackPanel();Content=panel;
        ProjectFields.Text(panel,"Codice","Project.Code",true);
        ProjectFields.Text(panel,"Anno","Project.Year",true);
        ProjectFields.Text(panel,"Numero","Project.Number",true);
        ProjectFields.Text(panel,"Committente rilevato nella cartella","Project.SourceCustomerName",true);
        panel.Children.Add(new TextBlock{Text="Cliente associato *",Margin=new Thickness(0,6,0,3)});
        var clients=new ComboBox{DisplayMemberPath="Name",SelectedValuePath="Id"};
        clients.SetBinding(ItemsControl.ItemsSourceProperty,new Binding("Details.Customers"));
        clients.SetBinding(ComboBox.SelectedValueProperty,new Binding("Project.CustomerId"){Mode=BindingMode.TwoWay});
        panel.Children.Add(clients);
        ProjectFields.Text(panel,"Argomento *","Project.Subject");
        ProjectFields.Text(panel,"Luogo (facoltativo)","Project.Location");
        ProjectFields.Choice(panel,"Stato (descrittivo)","Project.Status",new Dictionary<string,string>{{"","Non specificato"},{"Open","Aperta"},{"Closed","Chiusa"},{"Invoiced","Fatturata"}});
        panel.Children.Add(ProjectFields.Button("Salva commessa","SaveProjectCommand"));
    }
}

