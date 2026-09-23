using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace MinecraftServerManager;

public sealed class ServerDialog : Window
{
    private readonly TextBox name = new() { Text = "Новый сервер" };
    private readonly ComboBox edition = new() { ItemsSource = Enum.GetValues<ServerEdition>(), SelectedIndex = 0 };
    private readonly TextBox folder = new();
    private readonly TextBox port = new();
    private readonly TemplateStore templates;
    private readonly bool attach;
    public ServerProfile? Profile { get; private set; }
    public string? SourceFile { get; private set; }

    public ServerDialog(TemplateStore templates, bool attach, int bedrockPort, int javaPort)
    {
        this.templates = templates;
        this.attach = attach;
        Title = attach ? "Подключить сервер" : "Создать сервер";
        Width = 560; Height = 395; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel { Margin = new Thickness(22) };
        Content = body;
        Add(body, "Название", name);
        Add(body, "Редакция", edition);
        edition.SelectionChanged += (_, _) =>
        {
            port.Text = (edition.SelectedItem is ServerEdition.Java ? javaPort : bedrockPort).ToString();
        };
        port.Text = bedrockPort.ToString();
        Add(body, "Папка сервера", FolderRow(folder));
        Add(body, "Порт", port);
        if (!attach)
        {
            body.Children.Add(new TextBlock { Text = "Новый сервер будет создан из файла, добавленного в настройках шаблонов.", Foreground = (System.Windows.Media.Brush)Application.Current.Resources["MutedBrush"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 16) });
        }
        var submit = new Button { Content = attach ? "Подключить" : "Создать", Style = (Style)Application.Current.Resources["PrimaryButton"], HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        submit.Click += Submit;
        body.Children.Add(submit);
    }

    private static void Add(Panel panel, string label, UIElement control)
    {
        panel.Children.Add(new TextBlock { Text = label, Foreground = (System.Windows.Media.Brush)Application.Current.Resources["MutedBrush"] });
        if (control is FrameworkElement element) element.Margin = new Thickness(0, 4, 0, 13);
        panel.Children.Add(control);
    }
    private static UIElement FolderRow(TextBox box)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(box, 0); grid.Children.Add(box);
        var browse = new Button { Content = "Обзор…", Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "Папка сервера" };
            if (dialog.ShowDialog() == true) box.Text = dialog.FolderName;
        };
        Grid.SetColumn(browse, 1); grid.Children.Add(browse);
        return grid;
    }
    private void Submit(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("Введите название сервера.");
            if (string.IsNullOrWhiteSpace(folder.Text)) throw new InvalidOperationException("Выберите папку сервера.");
            if (!int.TryParse(port.Text, out var value) || value is < 1 or > 65535) throw new InvalidOperationException("Порт должен быть от 1 до 65535.");
            if (!attach && templates.GetPath((ServerEdition)edition.SelectedItem!) is null)
                throw new FileNotFoundException("Для этой редакции нет шаблона. Добавьте файл в настройках шаблонов.");
            var profile = new ServerProfile { Name = name.Text.Trim(), Directory = Path.GetFullPath(folder.Text.Trim()), Edition = (ServerEdition)edition.SelectedItem!, Port = value };
            if (attach && !File.Exists(Path.Combine(profile.Directory, profile.Edition == ServerEdition.Java ? "server.jar" : "bedrock_server.exe")))
                throw new FileNotFoundException("В выбранной папке не найден исполняемый файл сервера.");
            Profile = profile; SourceFile = attach ? null : templates.GetPath(profile.Edition);
            DialogResult = true;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Проверьте данные", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
