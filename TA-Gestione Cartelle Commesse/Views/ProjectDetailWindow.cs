using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TA_Gestione_Cartelle_Commesse.ViewModels;
namespace TA_Gestione_Cartelle_Commesse.Views;
public sealed class ProjectDetailWindow : Window
{
    public ProjectDetailWindow(ProjectDetailViewModel vm)
    {
        DataContext=vm;Title="Dettaglio commessa — TA";Width=1250;Height=850;MinWidth=950;MinHeight=600;
        var main=Application.Current.MainWindow;Resources=main.Resources;Background=main.Background;Foreground=main.Foreground;FontFamily=main.FontFamily;Icon=main.Icon;
        var dock=new DockPanel{Margin=new Thickness(18),Background=Background};Content=dock;
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};
        status.SetBinding(TextBlock.TextProperty,new Binding("Status"));DockPanel.SetDock(status,Dock.Bottom);dock.Children.Add(status);
        var head=new StackPanel{Orientation=Orientation.Horizontal};
        var title=new TextBlock{FontSize=26,Margin=new Thickness(0,0,20,12)};title.SetBinding(TextBlock.TextProperty,new Binding("Project.Code"));head.Children.Add(title);
        head.Children.Add(ProjectFields.Button("Aggiorna","RefreshCommand"));DockPanel.SetDock(head,Dock.Top);dock.Children.Add(head);
        var columns=new Grid();dock.Children.Add(columns);
        foreach(var _ in new[]{0,1,2})columns.ColumnDefinitions.Add(new ColumnDefinition());
        var project=new StackPanel{Margin=new Thickness(0,0,14,0)};
        var metadata=new WorkProjectView();metadata.SetBinding(IsEnabledProperty,new Binding("CanEdit"));project.Children.Add(metadata);
        var customer=new CustomerView();customer.SetBinding(DataContextProperty,new Binding("Customer"));project.Children.Add(customer);
        Add(columns,project,0);
        var phases=new StackPanel{Margin=new Thickness(0,0,14,0)};
        phases.Children.Add(new TextBlock{Text="Fasi della commessa",FontSize=20});
        var list=new ListBox{Height=160,DisplayMemberPath="Name"};
        list.SetBinding(ItemsControl.ItemsSourceProperty,new Binding("Details.Phases"));list.SetBinding(ListBox.SelectedItemProperty,new Binding("SelectedPhase"){Mode=BindingMode.TwoWay});phases.Children.Add(list);
        phases.Children.Add(ProjectFields.Button("Nuova fase","NewPhaseCommand"));
        var phaseEditor=new WorkPhaseView();phaseEditor.SetBinding(IsEnabledProperty,new Binding("CanEdit"));phases.Children.Add(phaseEditor);Add(columns,phases,1);
        var activities=new StackPanel();activities.Children.Add(new TextBlock{Text="Attività della fase",FontSize=20});
        var activityList=new ListBox{Height=160,DisplayMemberPath="Title"};activityList.SetBinding(ItemsControl.ItemsSourceProperty,new Binding("Activities"));activityList.SetBinding(ListBox.SelectedItemProperty,new Binding("SelectedActivity"){Mode=BindingMode.TwoWay});activities.Children.Add(activityList);
        activities.Children.Add(ProjectFields.Button("Nuova attività","NewActivityCommand"));
        var activityEditor=new WorkActivityView();activityEditor.SetBinding(IsEnabledProperty,new Binding("CanEdit"));activities.Children.Add(activityEditor);Add(columns,activities,2);
        Loaded+=async(_,_)=>await vm.Refresh();
    }
    private static void Add(Grid grid,UIElement content,int column)
    {
        var scroll=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        Grid.SetColumn(scroll,column);grid.Children.Add(scroll);
    }
}

