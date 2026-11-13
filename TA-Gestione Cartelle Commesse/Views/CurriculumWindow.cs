using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA_Gestione_Cartelle_Commesse.Views;

// Curriculum dello studio: elenco con ricerca a sinistra, scheda del lavoro a destra.
// Ogni regola è ripetuta dal server; le fotografie vengono ridotte prima dell'invio.
public sealed class CurriculumWindow : Window
{
    public sealed record Row(CurriculumWork Work)
    {
        public string Display => Work.Title + Environment.NewLine + string.Join(" · ", new[] { Work.Customer, CurriculumText.Period(Work), string.Join(", ", Work.ProjectCodes ?? []) }.Where(s => s.Length > 0));
    }
    private readonly ICurriculumGateway gateway;
    private readonly CurriculumWork? suggestion;
    private readonly TextBox search = new() { Margin = new Thickness(0, 0, 10, 0), MinWidth = 260 };
    private readonly ListBox list = new() { DisplayMemberPath = "Display", MinWidth = 320 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) }, links = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) }, author = new() { Margin = new Thickness(0, 6, 0, 0), Opacity = 0.7 };
    private readonly TextBox title = Box(), customer = Box(), place = Box(), services = Box(3), role = Box(), works = Box(), fee = Box(), categories = Box(4), reference = Box(2), description = Box(8), keywords = Box(), codes = Box(), projectSearch = Box();
    private readonly DatePicker start = new() { Margin = new Thickness(0, 4, 0, 10) }, end = new() { Margin = new Thickness(0, 4, 0, 10) };
    private readonly ListBox projectResults = new() { DisplayMemberPath = "Label", Height = 90, Margin = new Thickness(0, 0, 0, 6) };
    private readonly WrapPanel photos = new();
    private readonly Button save = new() { Content = "Salva" }, remove = new() { Content = "Elimina lavoro" }, addPhoto = new() { Content = "Aggiungi fotografie…" }, export = new() { Content = "Esporta in Word…" }, show = new() { Content = "Mostra la commessa nell'elenco" };
    private readonly Dictionary<string, TextBox> captions = new();
    private CurriculumSnapshot snapshot = new([], [], false);
    private CurriculumWork current = new("", "");
    private bool loading, dirty;
    public event Action<string>? ProjectRequested;
    public int VisibleCount => list.Items.Count;
    public string EditedTitle => title.Text;

    private static TextBox Box(int lines = 1) => new()
    {
        Margin = new Thickness(0, 4, 0, 10), AcceptsReturn = lines > 1, TextWrapping = lines > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap,
        MinLines = lines, MaxLines = Math.Max(lines, 1) * 3, VerticalScrollBarVisibility = lines > 1 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled
    };

    public CurriculumWindow(ICurriculumGateway gateway, string initialSearch = "", CurriculumWork? suggestion = null)
    {
        this.gateway = gateway; this.suggestion = suggestion; search.Text = initialSearch;
        Title = "Curriculum dello studio"; Width = 1280; Height = 860; MinWidth = 900; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Application.Current?.MainWindow is { } main && !ReferenceEquals(main, this))
        { Resources = main.Resources; Background = main.Background; Foreground = main.Foreground; FontFamily = main.FontFamily; Icon = main.Icon; }

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var create = new Button { Content = suggestion == null ? "+ Nuovo lavoro" : "+ Nuovo lavoro da " + string.Join(", ", suggestion.ProjectCodes ?? []) };
        var refresh = new Button { Content = "Aggiorna" };
        foreach (var button in new[] { export, refresh, create }) { DockPanel.SetDock(button, Dock.Right); top.Children.Add(button); }
        var label = new TextBlock { Text = "Cerca per titolo, committente, luogo, parole chiave, categoria, anno o codice commessa", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(label, Dock.Left); top.Children.Add(label); top.Children.Add(search);

        var form = new StackPanel { Margin = new Thickness(18, 0, 6, 0) };
        void Add(string text, UIElement control) { form.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }); form.Children.Add(control); }
        Add("Titolo del lavoro", title); Add("Committente", customer); Add("Luogo", place);
        var dates = new Grid(); dates.ColumnDefinitions.Add(new ColumnDefinition()); dates.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); dates.ColumnDefinitions.Add(new ColumnDefinition());
        var startPanel = new StackPanel(); startPanel.Children.Add(new TextBlock { Text = "Inizio" }); startPanel.Children.Add(start);
        var endPanel = new StackPanel(); endPanel.Children.Add(new TextBlock { Text = "Fine" }); endPanel.Children.Add(end); Grid.SetColumn(endPanel, 2);
        dates.Children.Add(startPanel); dates.Children.Add(endPanel); form.Children.Add(dates);
        Add("Prestazioni svolte (progettazione, direzione lavori, collaudo…)", services); Add("Ruolo dello studio (incaricato, mandatario, mandante, consulente…)", role);
        Add("Importo dei lavori (€)", works); Add("Importo del servizio (€)", fee);
        Add("Categorie delle opere — una per riga: ID opere; importo; descrizione (esempio: S.03; 250.000,00; Strutture in cemento armato)", categories);
        Add("Riferimenti del committente (RUP, referente, estremi dell'incarico o del certificato)", reference);
        Add("Descrizione", description); Add("Parole chiave per la ricerca, separate da virgola", keywords);
        Add("Commesse, gare o voci d'archivio collegate — codici separati da virgola (esempio: C24031, G23012)", codes);
        form.Children.Add(links);
        Add("Cerca una commessa da collegare: un clic sul risultato aggiunge il codice", projectSearch); form.Children.Add(projectResults); form.Children.Add(show);
        form.Children.Add(new TextBlock { Text = "Fotografie", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) });
        form.Children.Add(photos); form.Children.Add(addPhoto);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        actions.Children.Add(remove); actions.Children.Add(save); form.Children.Add(actions); form.Children.Add(author);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) }); content.ColumnDefinitions.Add(new ColumnDefinition());
        content.Children.Add(list);
        var scroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(scroll, 1); content.Children.Add(scroll);
        var root = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(top, Dock.Top); DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(top); root.Children.Add(status); root.Children.Add(content); Content = root;

        foreach (var box in new[] { title, customer, place, services, role, works, fee, categories, reference, description, keywords, codes }) box.TextChanged += (_, _) => { if (!loading) dirty = true; };
        start.SelectedDateChanged += (_, _) => { if (!loading) dirty = true; }; end.SelectedDateChanged += (_, _) => { if (!loading) dirty = true; };
        codes.TextChanged += (_, _) => ShowLinks();
        search.TextChanged += (_, _) => Filter();
        projectSearch.TextChanged += (_, _) => ShowProjects();
        projectResults.SelectionChanged += (_, _) =>
        {
            if (projectResults.SelectedItem is not CurriculumProject project) return;
            var known = CurriculumText.List(codes.Text);
            if (!known.Contains(project.Code, StringComparer.OrdinalIgnoreCase)) codes.Text = string.Join(", ", known.Append(project.Code));
            projectResults.SelectedItem = null;
        };
        list.SelectionChanged += (_, _) =>
        {
            if (loading || list.SelectedItem is not Row row || row.Work.Id == current.Id) return;
            if (!Leave()) { Select(current.Id); return; }
            Edit(row.Work);
        };
        create.Click += (_, _) => { if (Leave()) { Select(""); Edit(suggestion ?? new CurriculumWork("", "")); dirty = suggestion != null; title.Focus(); } };
        refresh.Click += async (_, _) => { if (Leave()) await Guard(LoadAsync); };
        save.Click += async (_, _) => await Guard(SaveAsync);
        remove.Click += async (_, _) => await Guard(DeleteAsync);
        addPhoto.Click += async (_, _) => await Guard(AddPhotosAsync);
        export.Click += async (_, _) => await Guard(ExportAsync);
        show.Click += (_, _) => { if (CurriculumText.List(codes.Text).FirstOrDefault() is { } code) ProjectRequested?.Invoke(code.ToUpperInvariant()); };
        Closing += (_, args) => { if (!Leave()) args.Cancel = true; };
        Loaded += async (_, _) => await Guard(LoadAsync);
        Edit(current);
    }

    private async Task Guard(Func<Task> action)
    {
        try { IsEnabled = false; await action(); }
        catch (Exception ex) { status.Text = "Operazione non riuscita: " + ex.Message; }
        finally { IsEnabled = true; }
    }
    private bool Leave() => !dirty || MessageBox.Show(this, "Le modifiche alla scheda non sono state salvate. Scartarle?", "Curriculum", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public async Task LoadAsync()
    {
        snapshot = await gateway.Send<CurriculumSnapshot>(new("Snapshot"));
        Filter();
        var selected = snapshot.Works.FirstOrDefault(w => w.Id == current.Id && current.Id.Length > 0);
        if (selected != null) Edit(selected); else if (current.Id.Length > 0) Edit(new CurriculumWork("", ""));
        ShowProjects();
        status.Text = $"{snapshot.Works.Length} lavori nel curriculum. " + (snapshot.TemplateConfigured ? "Esportazione Word con il modello dello studio." : "Modello Word dello studio non configurato sul server: l'esportazione usa un'impaginazione neutra.");
    }
    private void Filter()
    {
        loading = true;
        try { list.ItemsSource = CurriculumText.Match(snapshot.Works, search.Text).Select(w => new Row(w)).ToArray(); Select(current.Id); }
        finally { loading = false; }
    }
    private void Select(string id)
    {
        bool previous = loading; loading = true;
        try { list.SelectedItem = id.Length == 0 ? null : list.Items.OfType<Row>().FirstOrDefault(r => r.Work.Id == id); }
        finally { loading = previous; }
    }
    private void Edit(CurriculumWork work)
    {
        loading = true;
        try
        {
            current = work;
            title.Text = work.Title; customer.Text = work.Customer; place.Text = work.Place; services.Text = work.Services; role.Text = work.Role;
            start.SelectedDate = work.Start?.ToDateTime(TimeOnly.MinValue); end.SelectedDate = work.End?.ToDateTime(TimeOnly.MinValue);
            works.Text = CurriculumText.Amount(work.WorksAmount); fee.Text = CurriculumText.Amount(work.FeeAmount); categories.Text = CurriculumText.Categories(work.Categories);
            reference.Text = work.Reference; description.Text = work.Description; keywords.Text = string.Join(", ", work.Keywords ?? []); codes.Text = string.Join(", ", work.ProjectCodes ?? []);
            bool saved = work.Id.Length > 0;
            remove.IsEnabled = saved; addPhoto.IsEnabled = saved;
            addPhoto.ToolTip = saved ? null : "Salvare il lavoro prima di aggiungere fotografie";
            author.Text = saved && work.UpdatedUtc is { } at ? $"Ultima modifica: {work.UpdatedBy}, {at.ToLocalTime():dd/MM/yyyy HH:mm}" : "";
            dirty = false;
        }
        finally { loading = false; }
        ShowLinks(); _ = ShowPhotosAsync(work);
    }
    // Per ogni codice collegato dice dove si trova oggi la cartella: commesse attive, gare o archivio.
    private void ShowLinks()
    {
        var lines = CurriculumText.List(codes.Text).Select(code =>
        {
            var found = snapshot.Projects.Where(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase)).ToArray();
            return found.Length == 0 ? code.ToUpperInvariant() + " — non presente oggi tra commesse, gare e archivio"
                : string.Join(Environment.NewLine, found.Select(p => p.Label + " — " + (p.Category == "A" ? "in archivio" : p.Category == "G" ? "gara" : "commessa attiva")));
        }).ToArray();
        links.Text = string.Join(Environment.NewLine, lines); show.IsEnabled = lines.Length > 0;
    }
    private void ShowProjects()
    {
        var words = CurriculumText.Plain(projectSearch.Text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        projectResults.ItemsSource = words.Length == 0 ? Array.Empty<CurriculumProject>()
            : snapshot.Projects.Where(p => { var target = CurriculumText.Plain(p.Label); return words.All(word => target.Contains(word)); }).Take(40).ToArray();
    }
    private CurriculumWork Read() => current with
    {
        Title = title.Text, Customer = customer.Text, Place = place.Text, Services = services.Text, Role = role.Text, Reference = reference.Text, Description = description.Text,
        Start = start.SelectedDate is { } a ? (DateOnly?)DateOnly.FromDateTime(a) : null, End = end.SelectedDate is { } b ? (DateOnly?)DateOnly.FromDateTime(b) : null,
        WorksAmount = CurriculumText.Amount(works.Text, "Importo dei lavori"), FeeAmount = CurriculumText.Amount(fee.Text, "Importo del servizio"),
        Categories = CurriculumText.Categories(categories.Text), Keywords = CurriculumText.List(keywords.Text), ProjectCodes = CurriculumText.List(codes.Text).Select(c => c.ToUpperInvariant()).ToArray(),
        Photos = (current.Photos ?? []).Select(p => captions.TryGetValue(p.Id, out var box) ? p with { Caption = box.Text.Trim() } : p).ToArray()
    };
    public async Task SaveAsync()
    {
        var saved = await gateway.Send<CurriculumWork>(new("Save", Work: Read()));
        current = saved; dirty = false; await LoadAsync(); Select(saved.Id);
        status.Text = "Lavoro salvato: " + saved.Title;
    }
    private async Task DeleteAsync()
    {
        if (current.Id.Length == 0 || MessageBox.Show(this, "Eliminare dal curriculum il lavoro «" + current.Title + "» con le sue fotografie? Le cartelle delle commesse non vengono toccate.", "Curriculum", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await gateway.Send<bool>(new("Delete", Id: current.Id, Revision: current.Revision));
        dirty = false; Edit(new CurriculumWork("", "")); await LoadAsync(); status.Text = "Lavoro eliminato dal curriculum.";
    }
    private async Task AddPhotosAsync()
    {
        if (current.Id.Length == 0) return;
        if (dirty) { status.Text = "Salvare le modifiche alla scheda prima di aggiungere fotografie."; return; }
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Fotografie del lavoro", Filter = "Fotografie (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png", Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        int added = 0;
        foreach (var file in dialog.FileNames)
        {
            status.Text = "Invio di " + Path.GetFileName(file) + "…";
            var image = await Task.Run(() => PreparePhoto(File.ReadAllBytes(file)));
            current = await gateway.Send<CurriculumWork>(new("AddPhoto", Id: current.Id, Revision: current.Revision, Image: image, FileName: Path.GetFileName(file)));
            added++;
        }
        await LoadAsync(); status.Text = added + " fotografie aggiunte.";
    }
    private async Task ShowPhotosAsync(CurriculumWork work)
    {
        photos.Children.Clear(); captions.Clear();
        foreach (var photo in work.Photos ?? [])
        {
            var picture = new Image { Width = 180, Height = 130, Stretch = Stretch.Uniform };
            var caption = new TextBox { Text = photo.Caption, Width = 180, Margin = new Thickness(0, 4, 0, 4), ToolTip = "Didascalia" };
            caption.TextChanged += (_, _) => { if (!loading) dirty = true; };
            var delete = new Button { Content = "Rimuovi", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0) };
            delete.Click += async (_, _) => await Guard(async () =>
            {
                if (MessageBox.Show(this, "Rimuovere la fotografia dal curriculum?", "Curriculum", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                current = await gateway.Send<CurriculumWork>(new("DeletePhoto", Id: current.Id, Revision: current.Revision, PhotoId: photo.Id));
                await LoadAsync();
            });
            var cell = new StackPanel { Margin = new Thickness(0, 0, 12, 12) };
            cell.Children.Add(picture); cell.Children.Add(caption); cell.Children.Add(delete);
            photos.Children.Add(cell); captions[photo.Id] = caption;
            try
            {
                var bytes = await gateway.Send<byte[]>(new("Photo", Id: work.Id, PhotoId: photo.Id));
                if (current.Id != work.Id) return;
                picture.Source = Decode(bytes, 360);
            }
            catch (Exception ex) { picture.ToolTip = "Anteprima non disponibile: " + ex.Message; }
        }
    }
    private async Task ExportAsync()
    {
        var selection = list.Items.OfType<Row>().Select(r => r.Work).ToArray();
        if (selection.Length == 0) { status.Text = "Nessun lavoro da esportare: modificare la ricerca."; return; }
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Esporta il curriculum in Word", Filter = "Documento Word (*.docx)|*.docx", FileName = "Curriculum " + DateTime.Today.ToString("yyyy-MM-dd") + ".docx" };
        if (dialog.ShowDialog(this) != true) return;
        status.Text = $"Esportazione di {selection.Length} lavori…";
        byte[]? template = snapshot.TemplateConfigured ? await gateway.Send<byte[]>(new("Template")) : null;
        var pictures = new Dictionary<string, IReadOnlyList<CurriculumExportPhoto>>();
        foreach (var work in selection)
        {
            var items = new List<CurriculumExportPhoto>();
            foreach (var photo in work.Photos ?? [])
            {
                var bytes = await gateway.Send<byte[]>(new("Photo", Id: work.Id, PhotoId: photo.Id));
                var decoded = Decode(bytes, 0);
                items.Add(new(bytes, photo.Type, photo.Caption, decoded.PixelWidth, decoded.PixelHeight));
            }
            pictures[work.Id] = items;
        }
        var path = dialog.FileName;
        await Task.Run(() => CurriculumWordExport.Write(path, selection, work => pictures[work.Id], template));
        status.Text = $"Esportati {selection.Length} lavori (quelli mostrati dalla ricerca) in {path}";
    }
    private static BitmapSource Decode(byte[] bytes, int width)
    {
        var image = new BitmapImage();
        using var stream = new MemoryStream(bytes);
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; if (width > 0) image.DecodePixelWidth = width; image.StreamSource = stream; image.EndInit(); image.Freeze();
        return image;
    }
    // Riduce la fotografia al lato massimo indicato, applica l'orientamento registrato dalla fotocamera e la salva in JPEG.
    public static byte[] PreparePhoto(byte[] original, int maxSide = 1800)
    {
        using var input = new MemoryStream(original);
        var frame = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        BitmapSource source = frame;
        double angle = 0;
        try { if (frame.Metadata is BitmapMetadata metadata && metadata.ContainsQuery("/app1/ifd/{ushort=274}") && metadata.GetQuery("/app1/ifd/{ushort=274}") is ushort orientation) angle = orientation switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 }; }
        catch (Exception) { /* Formato senza dati della fotocamera: nessuna rotazione. */ }
        double scale = Math.Min(1.0, maxSide / (double)Math.Max(source.PixelWidth, source.PixelHeight));
        if (scale < 1.0 || angle != 0)
        {
            var transform = new TransformGroup(); transform.Children.Add(new ScaleTransform(scale, scale)); if (angle != 0) transform.Children.Add(new RotateTransform(angle));
            source = new TransformedBitmap(source, transform);
        }
        var encoder = new JpegBitmapEncoder { QualityLevel = 88 }; encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = new MemoryStream(); encoder.Save(output); return output.ToArray();
    }
}
