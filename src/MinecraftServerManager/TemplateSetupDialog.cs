using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MaterialDesignThemes.Wpf;

namespace MinecraftServerManager;

public sealed class TemplateSetupDialog : UserControl
{
    private readonly TaskCompletionSource<bool> completion = new();
    public Task<bool> Completion => completion.Task;
    private readonly TemplateStore store;
    private readonly bool firstRun;
    private readonly TextBlock heading = new() { FontSize = 24, FontWeight = FontWeights.Bold };
    private readonly TextBlock help = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button language = new() { HorizontalAlignment = HorizontalAlignment.Right };
    private readonly List<Action> refreshSections = [];
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox bedrock = new();
    private readonly TextBox java = new();
    private readonly TextBlock bedrockStatus = new();
    private readonly TextBlock javaStatus = new();
    private readonly Button save = new();
    private readonly Button close = new();
    private readonly ProgressBar progress = new() { Height = 4, IsIndeterminate = true, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 8) };

    public TemplateSetupDialog(TemplateStore store, bool firstRun)
    {
        this.store = store;
        this.firstRun = firstRun;
        Width = 680;
        var body = new StackPanel { Margin = new Thickness(24) };
        Content = body;
        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headingRow = new StackPanel { Orientation = Orientation.Horizontal };
        headingRow.Children.Add(new PackIcon { Kind = PackIconKind.FileUpload, Style = (Style)Application.Current.Resources["UiIcon"], Width = 24, Height = 24 });
        headingRow.Children.Add(heading);
        header.Children.Add(headingRow);
        language.ToolTip = T("Language");
        language.Click += (_, _) =>
        {
            try
            {
                Localization.Instance.SetLanguage(Localization.Instance.CurrentLanguage == "ru" ? "en" : "ru");
                RefreshLanguage();
            }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        Grid.SetColumn(language, 1);
        header.Children.Add(language);
        body.Children.Add(header);
        help.Foreground = Muted();
        help.Margin = new Thickness(0, 0, 0, 22);
        body.Children.Add(help);
        AddSection(body, "BedrockServerZip", "https://www.minecraft.net/en-us/download/server/bedrock", bedrock, bedrockStatus, ".zip");
        AddSection(body, "JavaServerJar", "https://www.minecraft.net/en-us/download/server", java, javaStatus, ".jar");
        error.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["GreenBrush"];
        body.Children.Add(error);
        body.Children.Add(progress);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        save.Content = IconContent.Create(PackIconKind.ContentSave, T("SaveFiles"));
        save.Style = (Style)Application.Current.Resources["PrimaryButton"];
        save.Click += Save;
        buttons.Children.Add(save);
        close.Content = IconContent.Create(PackIconKind.Close, firstRun ? T("Later") : T("Close"));
        close.Click += (_, _) => completion.TrySetResult(false);
        buttons.Children.Add(close);
        body.Children.Add(buttons);
        RefreshLanguage();
    }

    private static System.Windows.Media.Brush Muted() => (System.Windows.Media.Brush)Application.Current.Resources["MutedBrush"];
    private void AddSection(Panel parent, string titleKey, string url, TextBox path, TextBlock status, string extension)
    {
        var card = new Border { Background = (System.Windows.Media.Brush)Application.Current.Resources["PanelBrush"], BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["LineBrush"], BorderThickness = new Thickness(1), Padding = new Thickness(15), Margin = new Thickness(0, 0, 0, 14) };
        var content = new StackPanel(); card.Child = content; parent.Children.Add(card);
        var sectionTitle = new TextBlock { FontSize = 17, FontWeight = FontWeights.Bold };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new PackIcon { Kind = extension == ".zip" ? PackIconKind.Archive : PackIconKind.FileDocument, Style = (Style)Application.Current.Resources["UiIcon"] });
        titleRow.Children.Add(sectionTitle);
        content.Children.Add(titleRow);
        status.Foreground = Muted(); status.Margin = new Thickness(0, 4, 0, 11); content.Children.Add(status);
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        path.ToolTip = T("ChooseDownloadedFile"); Grid.SetColumn(path, 0); row.Children.Add(path);
        var browse = new Button { Content = IconContent.Create(PackIconKind.FolderOpen, T("ChooseFile")), Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Filter = $"{T(titleKey)}|*{extension}|{T("AllFiles")}|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (File.Exists(path.Text)) dialog.FileName = path.Text;
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) path.Text = dialog.FileName;
        };
        Grid.SetColumn(browse, 1); row.Children.Add(browse); content.Children.Add(row);
        var link = new Button { Content = IconContent.Create(PackIconKind.CloudDownload, T("OfficialDownload")), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        link.Click += (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        content.Children.Add(link);
        refreshSections.Add(() =>
        {
            sectionTitle.Text = T(titleKey);
            path.ToolTip = T("ChooseDownloadedFile");
            browse.Content = IconContent.Create(PackIconKind.FolderOpen, T("ChooseFile"));
            link.Content = IconContent.Create(PackIconKind.CloudDownload, T("OfficialDownload"));
        });
    }
    private void RefreshLanguage()
    {
        heading.Text = firstRun ? T("AddServerFiles") : T("ServerTemplates");
        help.Text = T("TemplateHelp");
        language.ToolTip = T("Language");
        language.Content = IconContent.Create(PackIconKind.Language, Localization.Instance.CurrentLanguage == "ru" ? "English" : "Русский");
        foreach (var refresh in refreshSections) refresh();
        save.Content = IconContent.Create(PackIconKind.ContentSave, T("SaveFiles"));
        close.Content = IconContent.Create(PackIconKind.Close, firstRun ? T("Later") : T("Close"));
        RefreshStatuses();
    }
    private void RefreshStatuses()
    {
        bedrockStatus.Text = store.Description(ServerEdition.Bedrock);
        javaStatus.Text = store.Description(ServerEdition.Java);
    }
    private async void Save(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(bedrock.Text) && string.IsNullOrWhiteSpace(java.Text) && !store.HasAny)
                throw new InvalidOperationException(T("ChooseAtLeastOneFile"));
            save.IsEnabled = false;
            close.IsEnabled = false;
            progress.Visibility = Visibility.Visible;
            save.Content = IconContent.Create(PackIconKind.ContentSave, T("Copying"));
            var bedrockPath = bedrock.Text.Trim();
            var javaPath = java.Text.Trim();
            await Task.Run(() =>
            {
                if (!string.IsNullOrWhiteSpace(bedrockPath)) store.Import(ServerEdition.Bedrock, bedrockPath);
                if (!string.IsNullOrWhiteSpace(javaPath)) store.Import(ServerEdition.Java, javaPath);
            });
            RefreshStatuses();
            completion.TrySetResult(true);
        }
        catch (Exception ex) { error.Text = ex.Message; }
        finally { save.IsEnabled = true; close.IsEnabled = true; progress.Visibility = Visibility.Collapsed; save.Content = IconContent.Create(PackIconKind.ContentSave, T("SaveFiles")); }
    }
}
