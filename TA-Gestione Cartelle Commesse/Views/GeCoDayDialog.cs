using System.Windows;
using System.Windows.Controls;
using TA_Gestione_Cartelle_Commesse.Services;
using TA_Gestione_Cartelle_Commesse.ViewModels;
using static TA_Gestione_Cartelle_Commesse.Views.GeCoUi;
namespace TA_Gestione_Cartelle_Commesse.Views;

// Note e dettagli della giornata: gli stessi campi del dettaglio della casella di GeCo2017 (ore, km, classificazione CDC, note
// e, per i centri esterni, i dati del SAL). «Applica» porta i valori tra le modifiche da salvare; il servizio li controlla.
internal sealed class GeCoDayDialog : Window
{
    public GeCoDayDialog(Window owner, HoursViewModel vm, HoursViewModel.Person p, HoursViewModel.Element e)
    {
        Prepare(this, e.Row.Codice + (e.Row.Fase.Length > 0 ? " · " + e.Row.Fase : "") + " – " + GeCoCalendar.Long(e.Cell.Data));
        Owner = owner; Width = 620; Height = 640; MinWidth = 480; MinHeight = 380; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        bool readOnly = !vm.Modifiable(p, e.Cell), kmEditable = vm.KmModifiable(p, e);
        var fields = new StackPanel { Margin = new Thickness(20) };
        if (e.Cell.Bloccata != null) fields.Children.Add(Notice(e.Cell.Bloccata));
        foreach (var m in new[] { vm.Errors.GetValueOrDefault(e.Key), e.KmKey != null && vm.Errors.TryGetValue(e.KmKey, out var k) ? "Km: " + k : null }.OfType<string>()) fields.Children.Add(Notice(m, "danger"));
        TextBox Line(string label, string? value, bool disabled, int max = 2000, bool lines = false)
        {
            fields.Children.Add(Caption(label, 13, false, null, false).With(t => t.Margin = new Thickness(0, 8, 0, 3)));
            var box = new TextBox { Text = value ?? "", IsReadOnly = disabled, MaxLength = max, MinHeight = 30, Padding = new Thickness(6, 3, 6, 3), AcceptsReturn = lines, TextWrapping = lines ? TextWrapping.Wrap : TextWrapping.NoWrap, Height = lines ? 80 : double.NaN };
            fields.Children.Add(box); return box;
        }
        var hours = Line("Ore", vm.Value(e.Key, e.Cell), readOnly, 6);
        var km = e.KmKey != null ? Line("Km", vm.Value(e.KmKey, e.KmCell), readOnly || !kmEditable, 6) : null;
        ComboBox? classes = null;
        if (p.Period?.Classificazioni.ElementAtOrDefault(e.Index) is { Opzioni.Length: > 0 } day)
        {
            fields.Children.Add(Caption("Classificazione CDC", 13, false, null, false).With(t => t.Margin = new Thickness(0, 8, 0, 3)));
            classes = new ComboBox { IsEnabled = !readOnly, MinHeight = 30 };
            var current = vm.Field(e.Key, e.Cell, "IdClass");
            classes.Items.Add(new ComboBoxItem { Content = day.Associate > 0 ? "(prima associata)" : "", Tag = "", IsSelected = string.IsNullOrEmpty(current) });
            foreach (var o in day.Opzioni.OrderByDescending(o => o.Associata))
                classes.Items.Add(new ComboBoxItem { Content = o.Valore + (o.Associata ? "" : " (non associata)"), Tag = o.Id.ToString(), IsSelected = current == o.Id.ToString() });
            fields.Children.Add(classes);
        }
        var note = Line("Note", vm.Field(e.Key, e.Cell, "Note"), readOnly, 2000, true);
        TextBox? sal = null, supplier = null, number = null, date = null, link = null;
        if (p.Period?.TipoCentro == "E")
        {
            sal = Line("Data rif. SAL (gg/mm/aaaa)", vm.Field(e.Key, e.Cell, "DataRifSal"), readOnly, 10);
            supplier = Line("Fornitore", vm.Field(e.Key, e.Cell, "Fornitore"), readOnly);
            number = Line("N° documento", vm.Field(e.Key, e.Cell, "NumDoc"), readOnly, 10);
            date = Line("Data documento (gg/mm/aaaa)", vm.Field(e.Key, e.Cell, "DataDoc"), readOnly, 10);
            link = Line("Collegamento fattura", vm.Field(e.Key, e.Cell, "LinkFattura"), readOnly);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 8, 20, 14) };
        if (!readOnly)
            buttons.Children.Add(Push("Applica", () =>
            {
                vm.Modify(e.Key, "Qta", GeCoCalendar.Clean(hours.Text)); vm.Modify(e.Key, "Note", note.Text);
                if (classes?.SelectedItem is ComboBoxItem { Tag: string id }) vm.Modify(e.Key, "IdClass", id);
                if (sal != null) { vm.Modify(e.Key, "DataRifSal", sal.Text.Trim()); vm.Modify(e.Key, "Fornitore", supplier!.Text); vm.Modify(e.Key, "NumDoc", number!.Text.Trim()); vm.Modify(e.Key, "DataDoc", date!.Text.Trim()); vm.Modify(e.Key, "LinkFattura", link!.Text.Trim()); }
                if (km != null && kmEditable && GeCoCalendar.Clean(km.Text) != vm.Value(e.KmKey!, e.KmCell)) vm.Modify(e.KmKey!, "Qta", GeCoCalendar.Clean(km.Text));
                DialogResult = true;
            }, true).With(b => b.IsDefault = true));
        buttons.Children.Add(Push("Chiudi", Close).With(b => b.IsCancel = true));
        var dock = new DockPanel(); DockPanel.SetDock(buttons, Dock.Bottom); dock.Children.Add(buttons);
        dock.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = dock;
        Loaded += (_, _) => { hours.Focus(); hours.SelectAll(); };
    }
}
