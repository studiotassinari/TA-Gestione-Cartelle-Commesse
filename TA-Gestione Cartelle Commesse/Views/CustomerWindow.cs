using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TA_Gestione_Cartelle_Commesse.ViewModels;
namespace TA_Gestione_Cartelle_Commesse.Views;
public sealed class CustomerWindow : Window
{
    private readonly Button use=new(){Content="Usa questo cliente",Margin=new Thickness(8),Visibility=Visibility.Collapsed};
    private Action<TA_Gestione_Cartelle_Commesse.Contracts.Customer>? selection;
    public event Action<TA_Gestione_Cartelle_Commesse.Contracts.WorkProject>? ProjectRequested;
    public void RequestSelection(Action<TA_Gestione_Cartelle_Commesse.Contracts.Customer>? action,string context="")
    { selection=action; use.Visibility=action==null?Visibility.Collapsed:Visibility.Visible;use.Content="Associa cliente a "+context; }
    public CustomerWindow(CustomerManagementViewModel vm,bool select=false,bool startNew=false)
    {
        DataContext=vm;Title="Clienti — Tassinari Associati";Width=1450;Height=800;MinWidth=1100;MinHeight=600;
        var main=Application.Current.MainWindow;Resources=main.Resources;Background=main.Background;Foreground=main.Foreground;FontFamily=main.FontFamily;Icon=main.Icon;
        var root=new DockPanel{Margin=new Thickness(24),Background=Background};Content=root;
        var header=new TextBlock{Text="Anagrafica clienti",FontSize=26,Margin=new Thickness(0,0,0,18)};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};status.SetBinding(TextBlock.TextProperty,new Binding("Status"));DockPanel.SetDock(status,Dock.Bottom);root.Children.Add(status);
        var actions=new StackPanel{Orientation=Orientation.Horizontal};
        actions.Children.Add(ProjectFields.Button("Nuovo cliente","NewCommand"));actions.Children.Add(ProjectFields.Button("Salva cliente","SaveCommand"));
        actions.Children.Add(ProjectFields.Button("Aggiorna elenco","RefreshCommand"));
        use.SetBinding(IsEnabledProperty,new Binding("CanSelect"));
        use.Click+=(_,_)=>{if(vm.Result is {} customer){selection?.Invoke(customer);RequestSelection(null);}};actions.Children.Add(use);
        DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(2,GridUnitType.Star)});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(2.3,GridUnitType.Star)});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(2.5,GridUnitType.Star)});root.Children.Add(grid);
        var selector=new CustomerSelectionView{Margin=new Thickness(0,0,24,0)};
        selector.SetBinding(DataContextProperty,new Binding("CustomerSelection"));grid.Children.Add(selector);
        var editor=new CustomerEditorView();editor.SetBinding(IsEnabledProperty,new Binding("CanEdit"));
        var scroll=new ScrollViewer{Content=editor,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(scroll,1);grid.Children.Add(scroll);
        var associated=new CustomerProjectsView{Margin=new Thickness(24,0,0,0)};
        Grid.SetColumn(associated,2);grid.Children.Add(associated);
        void OpenProject(TA_Gestione_Cartelle_Commesse.Contracts.WorkProject project)=>ProjectRequested?.Invoke(project);
        vm.ProjectRequested+=OpenProject;
        Closed+=(_,_)=>{vm.ProjectRequested-=OpenProject;vm.Dispose();selection=null;};
        Loaded+=async(_,_)=>{await vm.Refresh();if(startNew&&vm.NewCommand.CanExecute(null))vm.NewCommand.Execute(null);};
    }
}

