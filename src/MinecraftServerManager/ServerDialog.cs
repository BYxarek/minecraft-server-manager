using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MaterialDesignThemes.Wpf;

namespace MinecraftServerManager;

public sealed class ServerDialog : UserControl
{
    private readonly TaskCompletionSource<bool> completion = new();
    public Task<bool> Completion => completion.Task;
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox name = new() { Text = T("NewServer") };
    private readonly ComboBox edition = new();
    private readonly TextBox folder = new();
    private readonly TextBox port = new();
    private readonly TemplateStore templates;
    private readonly bool attach;
    private bool updatingDefaultFolder;
    private bool customFolder;
    public ServerProfile? Profile { get; private set; }
    public string? SourceFile { get; private set; }

    public ServerDialog(TemplateStore templates, bool attach, int bedrockPort, int javaPort)
    {
        this.templates = templates;
        this.attach = attach;
        edition.ItemsSource = attach ? Enum.GetValues<ServerEdition>() : Enum.GetValues<ServerEdition>().Where(x => templates.GetPath(x) != null).ToArray();
        edition.SelectedIndex = edition.Items.Count > 0 ? 0 : -1;
        Width = 560;
        var body = new StackPanel { Margin = new Thickness(22) };
        Content = body;
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        titleRow.Children.Add(new PackIcon { Kind = attach ? PackIconKind.FolderOpen : PackIconKind.Plus, Style = (Style)Application.Current.Resources["UiIcon"], Width = 24, Height = 24 });
        titleRow.Children.Add(new TextBlock { Text = attach ? T("AttachServer") : T("CreateServerTitle"), FontSize = 24, FontWeight = FontWeights.Bold });
        body.Children.Add(titleRow);
        Add(body, T("Name"), name);
        if (!attach)
        {
            name.TextChanged += (_, _) => { if (!customFolder) UpdateDefaultFolder(); };
            folder.TextChanged += (_, _) => { if (!updatingDefaultFolder) customFolder = true; };
            UpdateDefaultFolder();
        }
        Add(body, T("Edition"), edition);
        edition.SelectionChanged += (_, _) =>
        {
            port.Text = (edition.SelectedItem is ServerEdition.Java ? javaPort : bedrockPort).ToString();
        };
        port.Text = (edition.SelectedItem is ServerEdition.Java ? javaPort : bedrockPort).ToString();
        Add(body, T("ServerFolder"), FolderRow(folder));
        Add(body, T("Port"), port);
        if (!attach)
        {
            body.Children.Add(new TextBlock { Text = T("NewServerTemplateHelp"), Foreground = (System.Windows.Media.Brush)Application.Current.Resources["MutedBrush"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 16) });
        }
        error.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["GreenBrush"];
        body.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var submit = new Button { Content = IconContent.Create(attach ? PackIconKind.FolderOpen : PackIconKind.Plus, attach ? T("Attach") : T("Create")), Style = (Style)Application.Current.Resources["PrimaryButton"] };
        submit.Click += Submit;
        actions.Children.Add(submit);
        var cancel = new Button { Content = IconContent.Create(PackIconKind.Close, T("Cancel")) };
        cancel.Click += (_, _) => completion.TrySetResult(false);
        actions.Children.Add(cancel);
        body.Children.Add(actions);
    }

    private void UpdateDefaultFolder()
    {
        var safeName = string.Concat(name.Text.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(safeName)) safeName = T("NewServer");
        updatingDefaultFolder = true;
        try { folder.Text = Path.Combine(AppContext.BaseDirectory, safeName); }
        finally { updatingDefaultFolder = false; }
    }

    private static void Add(Panel panel, string label, UIElement control)
    {
        panel.Children.Add(new TextBlock { Text = label, Foreground = (System.Windows.Media.Brush)Application.Current.Resources["MutedBrush"] });
        if (control is FrameworkElement element) element.Margin = new Thickness(0, 4, 0, 13);
        panel.Children.Add(control);
    }
    private UIElement FolderRow(TextBox box)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(box, 0); grid.Children.Add(box);
        var browse = new Button { Content = IconContent.Create(PackIconKind.FolderOpen, T("Browse")), Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = T("ServerFolder") };
            if (Directory.Exists(box.Text)) dialog.InitialDirectory = box.Text;
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) { customFolder = true; box.Text = dialog.FolderName; }
        };
        Grid.SetColumn(browse, 1); grid.Children.Add(browse);
        return grid;
    }
    private void Submit(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException(T("EnterServerName"));
            if (string.IsNullOrWhiteSpace(folder.Text)) throw new InvalidOperationException(T("ChooseServerFolder"));
            if (edition.SelectedItem is not ServerEdition selectedEdition) throw new InvalidOperationException(T("NoTemplateForEdition"));
            if (!int.TryParse(port.Text, out var value) || value is < 1 or > 65535) throw new InvalidOperationException(T("PortRange"));
            if (!attach && templates.GetPath(selectedEdition) is null)
                throw new FileNotFoundException(T("NoTemplateForEdition"));
            var profile = new ServerProfile { Name = name.Text.Trim(), Directory = Path.GetFullPath(folder.Text.Trim()), Edition = selectedEdition, Port = value,
                JavaPath = selectedEdition == ServerEdition.Java ? JavaRuntime.DefaultPath() : "java" };
            if (attach && !File.Exists(Path.Combine(profile.Directory, profile.Edition == ServerEdition.Java ? "server.jar" : "bedrock_server.exe")))
                throw new FileNotFoundException(T("ServerExecutableMissing"));
            Profile = profile; SourceFile = attach ? null : templates.GetPath(profile.Edition);
            completion.TrySetResult(true);
        }
        catch (Exception ex) { error.Text = ex.Message; }
    }
}
