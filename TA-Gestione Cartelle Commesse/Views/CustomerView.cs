using System.Windows.Controls;
namespace TA_Gestione_Cartelle_Commesse.Views;
public sealed class CustomerView : UserControl
{
    public CustomerView()
    {
        var panel=new StackPanel();Content=panel;
        ProjectFields.Text(panel,"Cliente associato","Name",true);
        ProjectFields.Text(panel,"Indirizzo","Address",true);
        ProjectFields.Text(panel,"Partita IVA","Vat",true);
        ProjectFields.Text(panel,"Codice fiscale","TaxCode",true);
    }
}

