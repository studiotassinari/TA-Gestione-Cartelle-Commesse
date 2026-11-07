using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace TA_Gestione_Cartelle_Commesse.Views;
public sealed class WorkActivityView : UserControl
{
    public WorkActivityView()
    {
        var panel=new StackPanel();Content=panel;
        ProjectFields.Text(panel,"Attività della fase selezionata","Activity.Title");
        panel.Children.Add(new TextBlock{Text="Collaboratore"});
        var users=new ComboBox();users.SetBinding(ItemsControl.ItemsSourceProperty,new Binding("Details.Collaborators"));
        users.SetBinding(ComboBox.SelectedItemProperty,new Binding("Activity.Assignee"){Mode=BindingMode.TwoWay});panel.Children.Add(users);
        foreach(var field in new[]{"Start","End"})
        {
            panel.Children.Add(new TextBlock{Text=field=="Start"?"Inizio":"Fine",Margin=new Thickness(0,6,0,3)});
            var date=new DatePicker();date.SetBinding(DatePicker.SelectedDateProperty,new Binding("Activity."+field){Mode=BindingMode.TwoWay});panel.Children.Add(date);
        }
        ProjectFields.Text(panel,"Tempo previsto (minuti)","Activity.PlannedMinutes");
        ProjectFields.Text(panel,"Avanzamento (%)","Activity.Progress");
        ProjectFields.Choice(panel,"Stato attività","Activity.State",new Dictionary<string,string>{{"Planned","Pianificata"},{"Active","In corso"},{"Done","Completata"},{"Suspended","Sospesa"}});
        panel.Children.Add(ProjectFields.Button("Salva attività","SaveActivityCommand"));
    }
}

