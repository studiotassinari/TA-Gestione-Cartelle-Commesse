using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace TA_Gestione_Cartelle_Commesse.Views;
public sealed class WorkProjectView : UserControl
{
    public WorkProjectView()
    {
        var panel=new StackPanel();Content=panel;
        var identity=new Grid();
        foreach(var _ in new[]{0,1,2})identity.ColumnDefinitions.Add(new ColumnDefinition());
        var fields=new[]{("Codice","Project.Code"),("Anno","Project.Year"),("Numero","Project.NumberText")};
        for(var i=0;i<fields.Length;i++)
        {
            var field=new StackPanel{Margin=new Thickness(0,0,i==2?0:10,0)};
            ProjectFields.Text(field,fields[i].Item1,fields[i].Item2,true);Grid.SetColumn(field,i);identity.Children.Add(field);
        }
        panel.Children.Add(identity);
        panel.Children.Add(new TextBlock{Text="Cliente associato *",Margin=new Thickness(0,12,0,3)});
        var row=new DockPanel();
        var manage=ProjectFields.Button("Scheda cliente…","ManageCustomerCommand");DockPanel.SetDock(manage,Dock.Right);row.Children.Add(manage);
        var clients=new CustomerAutocompleteView{Margin=new Thickness(0,8,12,4)};
        clients.SetBinding(DataContextProperty,new Binding("CustomerSelection"));row.Children.Add(clients);panel.Children.Add(row);
        var source=new TextBlock{Margin=new Thickness(0,4,0,12)};
        source.SetBinding(TextBlock.TextProperty,new Binding("Project.SourceCustomerName"){StringFormat="Nome rilevato nella cartella: {0}"});panel.Children.Add(source);
        ProjectFields.Text(panel,"Argomento *","Project.Subject");
        ProjectFields.Text(panel,"Luogo (facoltativo)","Project.Location");
        ProjectFields.Choice(panel,"Stato (descrittivo)","Project.Status",new Dictionary<string,string>{{"","Non specificato"},{"Open","Aperta"},{"Closed","Chiusa"},{"Invoiced","Fatturata"}});

    }
}

