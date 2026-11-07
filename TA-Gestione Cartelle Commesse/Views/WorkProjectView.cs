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
        var fields=new[]{("Codice","Project.Code"),("Anno","Project.Year"),("Numero","Project.Number")};
        for(var i=0;i<fields.Length;i++)
        {
            var field=new StackPanel{Margin=new Thickness(0,0,i==2?0:10,0)};
            ProjectFields.Text(field,fields[i].Item1,fields[i].Item2,true);Grid.SetColumn(field,i);identity.Children.Add(field);
        }
        panel.Children.Add(identity);
        ProjectFields.Text(panel,"Committente rilevato nella cartella","Project.SourceCustomerName",true);
        panel.Children.Add(new TextBlock{Text="Cliente associato *",Margin=new Thickness(0,6,0,3)});
        var clients=new CustomerSelectionView{Height=210};
        clients.SetBinding(DataContextProperty,new Binding("CustomerSelection"));
        panel.Children.Add(clients);
        panel.Children.Add(ProjectFields.Button("Gestisci / scegli cliente…","ManageCustomerCommand"));
        ProjectFields.Text(panel,"Argomento *","Project.Subject");
        ProjectFields.Text(panel,"Luogo (facoltativo)","Project.Location");
        ProjectFields.Choice(panel,"Stato (descrittivo)","Project.Status",new Dictionary<string,string>{{"","Non specificato"},{"Open","Aperta"},{"Closed","Chiusa"},{"Invoiced","Fatturata"}});
        var save=ProjectFields.Button("Salva dati","SaveProjectCommand");
        save.SetBinding(ContentControl.ContentProperty,new Binding("SaveLabel"));panel.Children.Add(save);
    }
}

