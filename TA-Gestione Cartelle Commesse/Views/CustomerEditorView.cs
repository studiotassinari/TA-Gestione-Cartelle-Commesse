using System.Windows.Controls;
namespace TA_Gestione_Cartelle_Commesse.Views;

// Editor riutilizzabile: nessun accesso ai dati, solo binding al modello di presentazione del cliente.
public sealed class CustomerEditorView : UserControl
{
    public CustomerEditorView()
    {
        var editor=new StackPanel();Content=editor;
        ProjectFields.Text(editor,"Ragione sociale / nome","Editor.Name");
        ProjectFields.Text(editor,"Partita IVA","Editor.Vat");
        ProjectFields.Text(editor,"Codice fiscale","Editor.TaxCode");
        ProjectFields.Text(editor,"Indirizzo","Editor.Address");
        ProjectFields.Text(editor,"Email","Editor.Email");
    }
}
