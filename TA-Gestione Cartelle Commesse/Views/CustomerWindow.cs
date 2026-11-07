using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TA_Gestione_Cartelle_Commesse.ViewModels;
namespace TA_Gestione_Cartelle_Commesse.Views;
public sealed class CustomerWindow : Window
{
    public CustomerWindow(CustomerManagementViewModel vm,bool select=false,bool startNew=false)
    {
        DataContext=vm;Title="Clienti — Tassinari Associati";Width=1000;Height=650;MinWidth=800;MinHeight=550;
        var main=Application.Current.MainWindow;Resources=main.Resources;Background=main.Background;Foreground=main.Foreground;FontFamily=main.FontFamily;Icon=main.Icon;
        var root=new DockPanel{Margin=new Thickness(24),Background=Background};Content=root;
        var header=new TextBlock{Text="Anagrafica clienti",FontSize=26,Margin=new Thickness(0,0,0,18)};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};status.SetBinding(TextBlock.TextProperty,new Binding("Status"));DockPanel.SetDock(status,Dock.Bottom);root.Children.Add(status);
        var actions=new StackPanel{Orientation=Orientation.Horizontal};
        actions.Children.Add(ProjectFields.Button("Nuovo cliente","NewCommand"));actions.Children.Add(ProjectFields.Button("Salva cliente","SaveCommand"));
        if(select){var use=new Button{Content="Usa questo cliente",Margin=new Thickness(8)};use.SetBinding(IsEnabledProperty,new Binding("CanSelect"));use.Click+=(_,_)=>{if(vm.Result!=null)DialogResult=true;};actions.Children.Add(use);}
        DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(2,GridUnitType.Star)});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(3,GridUnitType.Star)});root.Children.Add(grid);
        var left=new DockPanel{Margin=new Thickness(0,0,24,0)};grid.Children.Add(left);
        var search=new TextBox{Margin=new Thickness(0,0,0,10),ToolTip="Cerca nome, partita IVA, codice fiscale, indirizzo o email"};
        search.SetBinding(TextBox.TextProperty,new Binding("Filter"){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});DockPanel.SetDock(search,Dock.Top);left.Children.Add(search);
        var list=new ListBox{DisplayMemberPath="Name"};list.SetBinding(ItemsControl.ItemsSourceProperty,new Binding("Rows"));list.SetBinding(ListBox.SelectedItemProperty,new Binding("Selected"){Mode=BindingMode.TwoWay});left.Children.Add(list);
        var editor=new StackPanel();editor.SetBinding(IsEnabledProperty,new Binding("CanEdit"));
        ProjectFields.Text(editor,"Ragione sociale / nome","Editor.Name");ProjectFields.Text(editor,"Partita IVA","Editor.Vat");ProjectFields.Text(editor,"Codice fiscale","Editor.TaxCode");ProjectFields.Text(editor,"Indirizzo","Editor.Address");ProjectFields.Text(editor,"Email","Editor.Email");
        var scroll=new ScrollViewer{Content=editor,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(scroll,1);grid.Children.Add(scroll);
        Loaded+=async(_,_)=>{await vm.Refresh();if(startNew&&vm.NewCommand.CanExecute(null))vm.NewCommand.Execute(null);};
    }
}

