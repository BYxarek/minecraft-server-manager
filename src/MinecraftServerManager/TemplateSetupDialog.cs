using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace MinecraftServerManager;

public sealed class TemplateSetupDialog : Window
{
    private readonly TemplateStore store;
    private readonly TextBox bedrock = new();
    private readonly TextBox java = new();
    private readonly TextBlock bedrockStatus = new();
    private readonly TextBlock javaStatus = new();

    public TemplateSetupDialog(TemplateStore store, bool firstRun)
    {
        this.store = store;
        Title = firstRun ? "Первый запуск · файлы серверов" : "Настройки · файлы серверов";
        Width = 680; Height = 540; MinWidth = 600; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel { Margin = new Thickness(24) };
        Content = body;
        body.Children.Add(new TextBlock { Text = firstRun ? "Добавьте серверные файлы" : "Шаблоны серверов", FontSize = 24, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        body.Children.Add(new TextBlock { Text = "Скачайте серверы с официального сайта и выберите ZIP для Bedrock, JAR для Java или оба файла. Приложение сохранит собственные копии для новых серверов. Существующие серверы не изменятся.", TextWrapping = TextWrapping.Wrap, Foreground = Muted(), Margin = new Thickness(0, 0, 0, 22) });
        AddSection(body, "Bedrock Dedicated Server · ZIP", "https://www.minecraft.net/en-us/download/server/bedrock", bedrock, bedrockStatus, "ZIP Bedrock|*.zip");
        AddSection(body, "Minecraft Java Server · JAR", "https://www.minecraft.net/en-us/download/server", java, javaStatus, "JAR Java|*.jar");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var save = new Button { Content = "Сохранить файлы", Style = (Style)Application.Current.Resources["PrimaryButton"] };
        save.Click += Save;
        buttons.Children.Add(save);
        var close = new Button { Content = firstRun ? "Позже" : "Закрыть" };
        close.Click += (_, _) => { DialogResult = false; };
        buttons.Children.Add(close);
        body.Children.Add(buttons);
        RefreshStatuses();
    }

    private static System.Windows.Media.Brush Muted() => (System.Windows.Media.Brush)Application.Current.Resources["MutedBrush"];
    private static void AddSection(Panel parent, string title, string url, TextBox path, TextBlock status, string filter)
    {
        var card = new Border { Background = (System.Windows.Media.Brush)Application.Current.Resources["PanelBrush"], BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["LineBrush"], BorderThickness = new Thickness(1), Padding = new Thickness(15), Margin = new Thickness(0, 0, 0, 14) };
        var content = new StackPanel(); card.Child = content; parent.Children.Add(card);
        content.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.Bold });
        status.Foreground = Muted(); status.Margin = new Thickness(0, 4, 0, 11); content.Children.Add(status);
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        path.ToolTip = "Выберите скачанный файл"; Grid.SetColumn(path, 0); row.Children.Add(path);
        var browse = new Button { Content = "Выбрать…", Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = filter + "|Все файлы|*.*" };
            if (dialog.ShowDialog() == true) path.Text = dialog.FileName;
        };
        Grid.SetColumn(browse, 1); row.Children.Add(browse); content.Children.Add(row);
        var link = new Button { Content = "Скачать с официального сайта", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        link.Click += (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        content.Children.Add(link);
    }
    private void RefreshStatuses()
    {
        bedrockStatus.Text = store.Description(ServerEdition.Bedrock);
        javaStatus.Text = store.Description(ServerEdition.Java);
    }
    private void Save(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(bedrock.Text) && string.IsNullOrWhiteSpace(java.Text) && !store.HasAny)
                throw new InvalidOperationException("Выберите хотя бы один файл сервера.");
            if (!string.IsNullOrWhiteSpace(bedrock.Text)) store.Import(ServerEdition.Bedrock, bedrock.Text.Trim());
            if (!string.IsNullOrWhiteSpace(java.Text)) store.Import(ServerEdition.Java, java.Text.Trim());
            RefreshStatuses();
            DialogResult = true;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Файл не добавлен", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
