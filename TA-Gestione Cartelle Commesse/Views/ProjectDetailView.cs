using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace TA_Gestione_Cartelle_Commesse.Views;
public sealed class ProjectDetailView : UserControl
{
    // geCo: canale verso il servizio per il team della commessa di GeCo (2.41.0); null = sezione non mostrata.
    public ProjectDetailView(TA_Gestione_Cartelle_Commesse.Services.IGeCoGateway? geCo=null)
    {
        var dock=new DockPanel{Margin=new Thickness(18),Background=Background};Content=dock;
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};
        status.SetBinding(TextBlock.TextProperty,new Binding("Status"));DockPanel.SetDock(status,Dock.Bottom);dock.Children.Add(status);
        var head=new StackPanel{Orientation=Orientation.Horizontal};
        var title=new TextBlock{FontSize=26,Margin=new Thickness(0,0,20,12)};title.SetBinding(TextBlock.TextProperty,new Binding("Project.Code"));head.Children.Add(title);
        head.Children.Add(ProjectFields.Button("💾 Salva","SaveProjectCommand"));
        head.Children.Add(ProjectFields.Button("↶ Annulla","RefreshCommand"));DockPanel.SetDock(head,Dock.Top);dock.Children.Add(head);
        var tabs=new TabControl();
        dock.Children.Add(tabs);
        var overview=new Grid{Margin=new Thickness(14)};
        System.Windows.Documents.TextElement.SetFontWeight(overview,FontWeights.Normal);
        overview.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(2,GridUnitType.Star)});
        
        var overviewTab=new TabItem{Content=overview};overviewTab.SetBinding(HeaderedContentControl.HeaderProperty,new Binding("OverviewTitle"));tabs.Items.Add(overviewTab);
        var project=new StackPanel{Margin=new Thickness(0,0,14,0)};
        project.Children.Add(new TextBlock{Text="Dati principali",FontSize=20,Margin=new Thickness(0,0,0,12)});
        var metadata=new WorkProjectView();metadata.SetBinding(IsEnabledProperty,new Binding("CanEdit"));project.Children.Add(metadata);
        Add(overview,project,0);
        // Team della commessa di GeCo (2.41.0): la commessa con lo stesso codice della cartella, solo per le commesse.
        if(geCo!=null)
        {
            overview.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star),MinWidth=320});
            var team=new ViewModels.ProjectTeamViewModel(geCo);
            var panel=new GeCoTeamPanel(team){VerticalAlignment=VerticalAlignment.Top};
            panel.EditRequested+=()=>new GeCoTeamWindow(team,shared:true){Owner=Window.GetWindow(this)}.ShowDialog();
            Add(overview,panel,1);
            string? shown=null;
            async void Show()
            {
                if(DataContext is not ViewModels.ProjectDetailViewModel{Details:{} d})return;
                var category=d.Project.Category;panel.Visibility=category=="C"?Visibility.Visible:Visibility.Collapsed;
                if(category!="C"||shown==d.Project.Code)return;
                shown=d.Project.Code;await team.ShowAsync(0,d.Project.Code);
            }
            void Watch(object? model){if(model is ViewModels.ProjectDetailViewModel vm)vm.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(vm.Details))Show();};}
            DataContextChanged+=(_,e)=>{Watch(e.NewValue);Show();};
            Unloaded+=(_,_)=>team.Dispose();
        }
        // Fasi e attività del vecchio archivio del gestionale non si mostrano più: la pianificazione è quella di GeCo (dalla 2.37.0).
    }
    private static void Add(Grid grid,UIElement content,int column)
    {
        var scroll=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        Grid.SetColumn(scroll,column);grid.Children.Add(scroll);
    }
}

