using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace MinecraftServerManager;

public partial class MainWindow : Window
{
    private readonly ProfileStore store = new();
    private readonly ServerManager manager = new();
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private ServerProfile? Selected => ServerList.SelectedItem as ServerProfile;
    private bool closing;
    private string? updateUrl;

    public MainWindow()
    {
        InitializeComponent();
        try { store.Load(); } catch (Exception ex) { ShowNotice("Не удалось загрузить список серверов: " + ex.Message); }
        manager.SetProfiles(store.Profiles);
        manager.Changed += id => Dispatcher.BeginInvoke(() => { if (Selected?.Id == id) Refresh(); });
        ServerList.ItemsSource = store.Profiles;
        if (store.Profiles.Count > 0) ServerList.SelectedIndex = 0;
        VersionText.Text = "Версия " + typeof(MainWindow).Assembly.GetName().Version?.ToString(3);
        refreshTimer.Tick += (_, _) => Refresh();
        refreshTimer.Start();
        Loaded += async (_, _) => await CheckForUpdates(false);
    }

    private void ShowNotice(string message)
    {
        NoticeText.Text = message;
        NoticeBorder.Visibility = Visibility.Visible;
    }
    private void ClearNotice() => NoticeBorder.Visibility = Visibility.Collapsed;
    private void Run(Action action)
    {
        try { action(); ClearNotice(); Refresh(); }
        catch (Exception ex) { ShowNotice(ex.Message); }
    }
    private async Task RunAsync(Func<Task> action)
    {
        try { await action(); ClearNotice(); Refresh(); }
        catch (Exception ex) { ShowNotice(ex.Message); }
    }
    private static int NextPort(IEnumerable<ServerProfile> profiles, ServerEdition edition)
    {
        var used = profiles.Where(p => p.Edition == edition).Select(p => p.Port).ToHashSet();
        var port = edition == ServerEdition.Java ? 25565 : 19132;
        while (used.Contains(port)) port++;
        return port;
    }
    private void AddServer(bool attach)
    {
        var dialog = new ServerDialog(attach, NextPort(store.Profiles, ServerEdition.Bedrock), NextPort(store.Profiles, ServerEdition.Java)) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Profile is null) return;
        Run(() =>
        {
            var profile = dialog.Profile;
            if (store.Profiles.Any(p => string.Equals(Path.GetFullPath(p.Directory).TrimEnd('\\'), profile.Directory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Эта папка уже подключена.");
            if (store.Profiles.Any(p => p.Edition == profile.Edition && p.Port == profile.Port))
                throw new InvalidOperationException("Этот порт уже занят другим профилем.");
            if (!attach) TemplateInstaller.Install(profile, dialog.SourceFile!);
            store.Profiles.Add(profile);
            store.Save();
            ServerList.Items.Refresh();
            ServerList.SelectedItem = profile;
        });
    }
    private void CreateButton_Click(object sender, RoutedEventArgs e) => AddServer(false);
    private void AttachButton_Click(object sender, RoutedEventArgs e) => AddServer(true);

    private void ServerList_SelectionChanged(object sender, SelectionChangedEventArgs e) => Refresh(true);
    private void Pages_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == Pages) Refresh();
    }
    private void Refresh(bool loadFields = false)
    {
        if (!IsLoaded) return;
        var profile = Selected;
        var has = profile != null;
        StartButton.IsEnabled = has && !manager.IsRunning(profile!);
        StopButton.IsEnabled = has && manager.IsRunning(profile!);
        RestartButton.IsEnabled = StopButton.IsEnabled;
        TitleText.Text = profile?.Name ?? "Выберите сервер";
        SubtitleText.Text = profile == null ? "Создавайте и управляйте серверами Bedrock и Java" : $"{profile.Edition} · {profile.Directory}";
        var running = has && manager.IsRunning(profile!);
        StateBadge.Text = running ? "● Работает" : "○ Остановлен";
        StateBadge.Foreground = (System.Windows.Media.Brush)FindResource(running ? "GreenBrush" : "MutedBrush");
        EditionValue.Text = profile?.Edition.ToString() ?? "—";
        MemoryValue.Text = running ? $"{manager.MemoryMb(profile!):0.0} МБ" : "—";
        EndpointValue.Text = profile?.Endpoint ?? "—";
        FolderValue.Text = profile?.Directory ?? "Папка не выбрана";
        PidValue.Text = running ? $"PID {manager.ProcessId(profile!)}" : "Процесс не запущен";
        if (profile == null) { ConsoleText.Text = ""; return; }
        if (Pages.SelectedIndex == 1)
        {
            var log = manager.GetLog(profile);
            if (ConsoleText.Text != log) { ConsoleText.Text = log; ConsoleText.ScrollToEnd(); }
        }
        if (loadFields)
        {
            NameBox.Text = profile.Name;
            PortBox.Text = profile.Port.ToString();
            MemoryBox.Text = profile.MemoryMb.ToString();
            JavaBox.Text = profile.JavaPath;
            AutoRestartBox.IsChecked = profile.AutoRestart;
            PropertiesBox.Text = PropertiesFile.Read(profile);
            BackupList.ItemsSource = System.IO.Directory.Exists(Path.Combine(profile.Directory, "backups"))
                ? System.IO.Directory.GetFiles(Path.Combine(profile.Directory, "backups"), "*.zip").OrderByDescending(x => x).Select(Path.GetFileName).ToList() : [];
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } profile) Run(() => manager.Start(profile));
    }
    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } profile) await RunAsync(() => manager.Stop(profile));
    }
    private async void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } profile) await RunAsync(async () => { await manager.Stop(profile); manager.Start(profile); });
    }
    private void SendButton_Click(object sender, RoutedEventArgs e) => SendCommand();
    private void CommandText_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { SendCommand(); e.Handled = true; }
    }
    private void SendCommand()
    {
        if (Selected is not { } profile) return;
        Run(() => { manager.Send(profile, CommandText.Text.Trim()); CommandText.Clear(); });
    }
    private void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } profile) return;
        Run(() =>
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text)) throw new InvalidOperationException("Введите название.");
            if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535) throw new InvalidOperationException("Некорректный порт.");
            if (store.Profiles.Any(p => p.Id != profile.Id && p.Edition == profile.Edition && p.Port == port)) throw new InvalidOperationException("Порт занят другим профилем.");
            if (!int.TryParse(MemoryBox.Text, out var memory) || memory is < 512 or > 131072) throw new InvalidOperationException("Память Java должна быть от 512 до 131072 МБ.");
            if (manager.IsRunning(profile) && profile.Port != port) throw new InvalidOperationException("Остановите сервер перед изменением порта.");
            profile.Name = NameBox.Text.Trim(); profile.Port = port; profile.MemoryMb = memory;
            profile.JavaPath = string.IsNullOrWhiteSpace(JavaBox.Text) ? "java" : JavaBox.Text.Trim();
            profile.AutoRestart = AutoRestartBox.IsChecked == true;
            PropertiesFile.Set(profile, "server-port", port.ToString());
            store.Save(); ServerList.Items.Refresh();
        });
    }
    private void SavePropertiesButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } profile) Run(() => PropertiesFile.Write(profile, PropertiesBox.Text));
    }
    private void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } profile) return;
        Run(() => { var path = manager.CreateBackup(profile); Refresh(true); ShowNotice("Копия создана: " + path); });
    }
    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } profile) Run(() => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{profile.Directory}\"") { UseShellExecute = true }));
    }
    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } profile) return;
        if (manager.IsRunning(profile)) { ShowNotice("Остановите сервер перед удалением из списка."); return; }
        if (MessageBox.Show(this, "Убрать сервер из списка? Файлы на диске останутся.", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Run(() => { store.Profiles.Remove(profile); store.Save(); ServerList.Items.Refresh(); ServerList.SelectedIndex = store.Profiles.Count > 0 ? 0 : -1; });
    }
    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e) => await CheckForUpdates(true);
    private async Task CheckForUpdates(bool showError)
    {
        UpdateStatus.Text = "Проверка обновлений…";
        var result = await UpdateChecker.Check();
        updateUrl = result.Available ? result.Url : null;
        UpdateStatus.Text = result.Available ? $"Доступна версия {result.Latest}. Нажмите, чтобы открыть релиз." : result.Error != null ? "Не удалось проверить обновления." : "Установлена актуальная версия.";
        if (result.Error != null && showError) ShowNotice("GitHub: " + result.Error);
        if (result.Available) UpdateStatus.MouseLeftButtonUp += (_, _) => Process.Start(new ProcessStartInfo(updateUrl!) { UseShellExecute = true });
    }
    protected override async void OnClosing(CancelEventArgs e)
    {
        if (closing) { base.OnClosing(e); return; }
        e.Cancel = true;
        closing = true;
        IsEnabled = false;
        refreshTimer.Stop();
        await RunAsync(manager.StopAll);
        Close();
    }
}
