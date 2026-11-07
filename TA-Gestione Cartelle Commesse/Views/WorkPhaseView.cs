using System.Windows.Controls;
namespace TA_Gestione_Cartelle_Commesse.Views;
public sealed class WorkPhaseView : UserControl
{
    public WorkPhaseView()
    {
        var panel=new StackPanel();Content=panel;
        ProjectFields.Text(panel,"Nome fase","Phase.Name");
        ProjectFields.Text(panel,"Descrizione","Phase.Description");
        ProjectFields.Text(panel,"Ordine","Phase.Order");
        panel.Children.Add(ProjectFields.Button("Salva fase","SavePhaseCommand"));
    }
}

