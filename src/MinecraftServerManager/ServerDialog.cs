using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Diagnostics;

namespace MinecraftServerManager;

public sealed class ServerDialog : Window
{
    private readonly TextBox name = new() { Text = "Новый сервер" };
    private readonly ComboBox edition = new() { ItemsSource = Enum.GetValues<ServerEdition>(), SelectedIndex = 0 };
    private readonly TextBox folder = new();
    private readonly TextBox port = new();
    private readonly TextBox source = new();
    private readonly bool attach;
    public ServerProfile? Profile { get; private set; }
    public string? SourceFile { get; private set; }

    public ServerDialog(bool attach, int bedrockPort, int javaPort)
    {
        this.attach = attach;
        Title = attach ? "Подключить сервер" : "Создать сервер";
        Width = 560; Height = attach ? 410 : 500; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel { Margin = new Thickness(22) };
        Content = body;
        Add(body, "Название", name);
        Add(body, "Редакция", edition);
        edition.SelectionChanged += (_, _) =>
        {
            port.Text = (edition.SelectedItem is ServerEdition.Java ? javaPort : bedrockPort).ToString();
            if (!attach) source.Text = TemplateInstaller.FindLocalTemplate((ServerEdition)edition.SelectedItem!) ?? "";
        };
        port.Text = bedrockPort.ToString();
        if (!attach) source.Text = TemplateInstaller.FindLocalTemplate(ServerEdition.Bedrock) ?? "";
        Add(body, "Папка сервера", FolderRow(folder, false));
        Add(body, "Порт", port);
        if (!attach)
        {
            Add(body, "ZIP Bedrock или JAR Java", FolderRow(source, true));
            body.Children.Add(new TextBlock { Text = "Локальный шаблон подставляется автоматически. Можно выбрать скачанный официальный файл.", Foreground = (System.Windows.Media.Brush)Application.Current.Resources["MutedBrush"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 16) });
            var download = new Button { Content = "Официальная загрузка", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
            download.Click += (_, _) => Process.Start(new ProcessStartInfo(edition.SelectedItem is ServerEdition.Java ? "https://www.minecraft.net/en-us/download/server" : "https://www.minecraft.net/en-us/download/server/bedrock") { UseShellExecute = true });
            body.Children.Add(download);
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
    private static UIElement FolderRow(TextBox box, bool file)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(box, 0); grid.Children.Add(box);
        var browse = new Button { Content = "Обзор…", Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            if (file)
            {
                var dialog = new OpenFileDialog { Filter = "Сервер Minecraft|*.zip;*.jar|Все файлы|*.*" };
                if (dialog.ShowDialog() == true) box.Text = dialog.FileName;
            }
            else
            {
                var dialog = new OpenFolderDialog { Title = "Папка сервера" };
                if (dialog.ShowDialog() == true) box.Text = dialog.FolderName;
            }
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
            if (!attach && !File.Exists(source.Text)) throw new FileNotFoundException("Выберите файл сервера.");
            var profile = new ServerProfile { Name = name.Text.Trim(), Directory = Path.GetFullPath(folder.Text.Trim()), Edition = (ServerEdition)edition.SelectedItem!, Port = value };
            if (attach && !File.Exists(Path.Combine(profile.Directory, profile.Edition == ServerEdition.Java ? "server.jar" : "bedrock_server.exe")))
                throw new FileNotFoundException("В выбранной папке не найден исполняемый файл сервера.");
            Profile = profile; SourceFile = attach ? null : source.Text;
            DialogResult = true;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Проверьте данные", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
