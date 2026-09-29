using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using MaterialDesignThemes.Wpf;

namespace MinecraftServerManager;

public partial class MainWindow : Window
{
    private readonly ProfileStore store = new();
    private readonly TemplateStore templates = new();
    private readonly ServerManager manager = new();
    private readonly UpdateChecker updateChecker = new();
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer backupTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly HashSet<Guid> busyBackups = [];
    private readonly Dictionary<Guid, DateTime> backupRetryAfter = new();
    private ServerProfile? editorProfile;
    private string? profileSnapshot;
    private string? propertiesSnapshot;
    private bool revertingSelection;
    private bool backupIntervalLoading;
    private bool installing;
    private bool deleting;
    private bool initialLoadStarted;
    private ServerProfile? Selected => ServerList.SelectedItem as ServerProfile;
    private bool closing;
    private bool exitRequested;
    private bool allowListLoading;
    private DateTime lastPlayerRefreshUtc;
    private string? noticeBackupPath;
    private readonly System.Windows.Forms.NotifyIcon trayIcon = new();
    private string? updateUrl;
    private UpdateResult? lastUpdateResult;

    public MainWindow()
    {
        InitializeComponent();
        var trayMenu = new System.Windows.Forms.ContextMenuStrip();
        trayMenu.Items.Add(T("ShowWindow"), null, (_, _) => Dispatcher.Invoke(RestoreWindow));
        trayMenu.Items.Add(T("ExitApplication"), null, (_, _) => Dispatcher.Invoke(RequestExit));
        trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        trayIcon.Text = "Minecraft Server Manager";
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreWindow);
        trayIcon.Visible = true;
        Closed += (_, _) => { trayIcon.Visible = false; trayIcon.Dispose(); trayMenu.Dispose(); };
        SourceInitialized += (_, _) => EnableDarkTitleBar();
        Localization.Instance.PropertyChanged += (_, _) =>
        {
            var languageIndex = Localization.Instance.CurrentLanguage == "ru" ? 1 : 0;
            if (LanguageBox.SelectedIndex != languageIndex) LanguageBox.SelectedIndex = languageIndex;
            trayMenu.Items[0].Text = T("ShowWindow");
            trayMenu.Items[1].Text = T("ExitApplication");
        };
        LanguageBox.SelectedIndex = Localization.Instance.CurrentLanguage == "ru" ? 1 : 0;
        UpdateStatus.MouseLeftButtonUp += (_, _) => { if (updateUrl != null) Process.Start(new ProcessStartInfo(updateUrl) { UseShellExecute = true }); };
        try { store.Load(); } catch (Exception ex) { ShowNotice(F("LoadProfilesFailed", ex.Message)); }
        manager.SetProfiles(store.Profiles);
        manager.Changed += id => Dispatcher.BeginInvoke(() => { if (Selected?.Id == id) Refresh(); });
        ServerList.ItemsSource = store.Profiles;
        if (store.Profiles.Count > 0) ServerList.SelectedIndex = 0;
        RenderVersion();
        refreshTimer.Tick += (_, _) => { Refresh(); RefreshPlayersIfNeeded(); };
        refreshTimer.Start();
        backupTimer.Tick += async (_, _) => await RunScheduledBackups();
        backupTimer.Start();
        Loaded += async (_, _) =>
        {
            if (initialLoadStarted) return;
            initialLoadStarted = true;
            Refresh(true);
            if (!templates.HasAny)
            {
                var setup = new TemplateSetupDialog(templates, true);
                await ShowInlineAsync(setup, setup.Completion);
                if (!templates.HasAny) ShowNotice(T("AddTemplateHint"));
            }
            await CheckForUpdates(false);
        };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void EnableDarkTitleBar()
    {
        var enabled = 1;
        var handle = new WindowInteropHelper(this).Handle;
        // Windows 10/11 use attribute 20; older Windows 10 builds use 19.
        if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
        var greenText = 0x00A5C4A7;
        DwmSetWindowAttribute(handle, 36, ref greenText, sizeof(int));
    }

    private void ShowNotice(string message)
    {
        noticeBackupPath = null;
        NoticeOpenBackupButton.Visibility = Visibility.Collapsed;
        NoticeText.Text = message;
        ActivityRow.Visibility = Visibility.Collapsed;
        NoticeBorder.Visibility = Visibility.Visible;
    }
    private void ShowBackupNotice(string message, string path)
    {
        ShowNotice(message);
        noticeBackupPath = path;
        NoticeOpenBackupButton.Visibility = Visibility.Visible;
    }
    private void ShowBusy(string message)
    {
        ShowNotice(message);
        ActivityRow.Visibility = Visibility.Visible;
    }
    private void ClearNotice() { NoticeBorder.Visibility = Visibility.Collapsed; ActivityRow.Visibility = Visibility.Collapsed; NoticeOpenBackupButton.Visibility = Visibility.Collapsed; noticeBackupPath = null; }
    private async Task<bool> ShowInlineAsync(UserControl panel, Task<bool> completion)
    {
        InlineContent.Content = panel;
        InlineOverlay.Visibility = Visibility.Visible;
        try { return await completion; }
        finally { InlineOverlay.Visibility = Visibility.Collapsed; InlineContent.Content = null; }
    }
    private Task<bool> ConfirmAsync(string title, string message)
    {
        var completion = new TaskCompletionSource<bool>();
        var content = new StackPanel { Margin = new Thickness(24), Width = 500 };
        content.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) });
        content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var yes = new Button { Content = IconContent.Create(PackIconKind.Check, T("Yes")), Style = (Style)FindResource("PrimaryButton") };
        yes.Click += (_, _) => completion.TrySetResult(true);
        var no = new Button { Content = IconContent.Create(PackIconKind.Close, T("No")) };
        no.Click += (_, _) => completion.TrySetResult(false);
        actions.Children.Add(yes); actions.Children.Add(no); content.Children.Add(actions);
        return ShowInlineAsync(new UserControl { Content = content }, completion.Task);
    }
    private void RenderVersion() => VersionText.Text = F("Version", typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "?");
    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || LanguageBox.SelectedItem is not ComboBoxItem item) return;
        try
        {
            Localization.Instance.SetLanguage((string)item.Tag);
            RenderVersion();
            RenderUpdateStatus();
            Refresh();
            UpdateConsoleSearch();
            if (Selected != null && busyBackups.Contains(Selected.Id)) BackupProgressText.Text = T("BackupOperation");
            ClearNotice();
        }
        catch (Exception ex)
        {
            LanguageBox.SelectedIndex = Localization.Instance.CurrentLanguage == "ru" ? 1 : 0;
            ShowNotice(ex.Message);
        }
    }
    private string CurrentProfileSnapshot() => string.Join("\u001f", NameBox.Text, PortBox.Text, MemoryBox.Text, JavaBox.Text, AutoRestartBox.IsChecked == true);
    private bool EditorDirty() => editorProfile != null && (profileSnapshot != CurrentProfileSnapshot() || propertiesSnapshot != PropertiesBox.Text || SimpleEditor.IsDirty);
    private void RememberEditor() { editorProfile = Selected; profileSnapshot = CurrentProfileSnapshot(); propertiesSnapshot = PropertiesBox.Text; }
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
    private async Task AddServer(bool attach)
    {
        var dialog = new ServerDialog(templates, attach, NextPort(store.Profiles, ServerEdition.Bedrock), NextPort(store.Profiles, ServerEdition.Java));
        if (!await ShowInlineAsync(dialog, dialog.Completion) || dialog.Profile is null) return;
        try
        {
            var profile = dialog.Profile;
            if (store.Profiles.Any(p => string.Equals(Path.GetFullPath(p.Directory).TrimEnd('\\'), profile.Directory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(T("FolderAlreadyAttached"));
            if (store.Profiles.Any(p => p.Edition == profile.Edition && p.Port == profile.Port))
                throw new InvalidOperationException(T("PortAlreadyUsed"));
            if (!attach)
            {
                installing = true;
                CreateButton.IsEnabled = false;
                ShowBusy(T("InstallingServer"));
                await Task.Run(() => TemplateInstaller.Install(profile, dialog.SourceFile!));
            }
            store.Profiles.Add(profile);
            try { store.Save(); }
            catch { store.Profiles.Remove(profile); throw; }
            ServerList.Items.Refresh();
            ServerList.SelectedItem = profile;
            ClearNotice();
        }
        catch (Exception ex) { ShowNotice(ex.Message); }
        finally { installing = false; CreateButton.IsEnabled = true; Refresh(); }
    }
    private async void CreateButton_Click(object sender, RoutedEventArgs e) => await AddServer(false);
    private async void AttachButton_Click(object sender, RoutedEventArgs e) => await AddServer(true);
    private async void TemplatesButton_Click(object sender, RoutedEventArgs e)
    {
        var setup = new TemplateSetupDialog(templates, false);
        await ShowInlineAsync(setup, setup.Completion);
        if (templates.HasAny) ClearNotice();
    }

    private async void ServerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (revertingSelection) return;
        if (editorProfile != null && editorProfile != Selected && EditorDirty())
        {
            var requested = Selected;
            revertingSelection = true;
            ServerList.SelectedItem = editorProfile;
            revertingSelection = false;
            if (!await ConfirmAsync(T("UnsavedTitle"), T("SwitchDiscard"))) return;
            revertingSelection = true;
            ServerList.SelectedItem = requested;
            revertingSelection = false;
        }
        Refresh(true);
    }
    private void Pages_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == Pages) Refresh();
    }
    private void Refresh(bool loadFields = false)
    {
        if (!IsLoaded) return;
        var profile = Selected;
        var has = profile != null;
        Pages.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        NoSelectionPanel.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        var state = has ? manager.State(profile!) : ServerState.Stopped;
        var busy = has && busyBackups.Contains(profile!.Id);
        BackupProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StartButton.IsEnabled = has && (state is ServerState.Stopped or ServerState.Failed) && !busy;
        StopButton.IsEnabled = has && (state is ServerState.Running or ServerState.Restarting);
        RestartButton.IsEnabled = has && state == ServerState.Running;
        BackupButton.IsEnabled = has && (state is ServerState.Stopped or ServerState.Failed) && !busy;
        RemoveButton.IsEnabled = has && !busy && !deleting && state is (ServerState.Stopped or ServerState.Failed or ServerState.Restarting);
        RestoreButton.IsEnabled = BackupButton.IsEnabled && BackupList.SelectedItem != null;
        OpenBackupButton.IsEnabled = has && BackupList.SelectedItem != null;
        RefreshPlayersButton.IsEnabled = has && manager.IsRunning(profile!);
        var playerSelected = has && OnlinePlayersList.SelectedItem is string && manager.IsRunning(profile!);
        KickPlayerButton.IsEnabled = playerSelected;
        OpPlayerButton.IsEnabled = playerSelected;
        DeopPlayerButton.IsEnabled = playerSelected;
        BanPlayerButton.IsEnabled = playerSelected && profile!.Edition == ServerEdition.Java;
        PardonRow.Visibility = has && profile!.Edition == ServerEdition.Java ? Visibility.Visible : Visibility.Collapsed;
        if (PlayerGameModeBox.Items.Count > 3 && PlayerGameModeBox.Items[3] is ComboBoxItem spectator)
        {
            spectator.Visibility = has && profile!.Edition == ServerEdition.Java ? Visibility.Visible : Visibility.Collapsed;
            if (spectator.Visibility == Visibility.Collapsed && PlayerGameModeBox.SelectedItem == spectator) PlayerGameModeBox.SelectedIndex = 0;
        }
        AllowOnlinePlayerButton.IsEnabled = playerSelected;
        SetPlayerGameModeButton.IsEnabled = playerSelected;
        MessagePlayerButton.IsEnabled = playerSelected;
        RemoveAllowListButton.IsEnabled = has && AllowListNames.SelectedItem is string;
        if (has) RefreshOnlinePlayers(profile!);
        TitleText.Text = profile?.Name ?? T("SelectServer");
        SubtitleText.Text = profile == null ? T("AppSubtitle") : $"{profile.Edition} · {profile.Directory}";
        var running = has && manager.IsRunning(profile!);
        StateBadge.Text = state switch { ServerState.Starting => T("Starting"), ServerState.Running => T("ProcessRunning"), ServerState.Stopping => T("Stopping"), ServerState.Restarting => T("WaitingRestart"), ServerState.Failed => T("Failed"), _ => T("Stopped") };
        StateBadge.Foreground = (System.Windows.Media.Brush)FindResource(running ? "GreenBrush" : "MutedBrush");
        EditionValue.Text = profile?.Edition.ToString() ?? "—";
        MemoryValue.Text = running ? F("MemoryMb", manager.MemoryMb(profile!)) : "—";
        EndpointValue.Text = profile?.Endpoint ?? "—";
        FolderValue.Text = profile?.Directory ?? T("NoFolder");
        PidValue.Text = running ? F("Pid", manager.ProcessId(profile!)) : T("ProcessNotRunning");
        if (profile == null) { ConsoleText.Text = ""; SearchCountText.Text = ""; BackupProgressText.Text = ""; editorProfile = null; BackupList.ItemsSource = null; AllowListNames.ItemsSource = null; OnlinePlayersList.ItemsSource = null; return; }
        if (Pages.SelectedIndex == 1)
        {
            var log = manager.GetLog(profile);
            if (ConsoleText.Text != log) { ConsoleText.Text = log; ConsoleText.ScrollToEnd(); UpdateConsoleSearch(); }
        }
        if (loadFields)
        {
            JavaSettingsPanel.Visibility = profile.Edition == ServerEdition.Java ? Visibility.Visible : Visibility.Collapsed;
            NameBox.Text = profile.Name;
            PortBox.Text = profile.Port.ToString();
            MemoryBox.Text = profile.MemoryMb.ToString();
            JavaBox.Text = JavaRuntime.Resolve(profile.JavaPath);
            AutoRestartBox.IsChecked = profile.AutoRestart;
            PropertiesBox.Text = PropertiesFile.Read(profile);
            SimpleEditor.Load(profile.Edition, PropertiesBox.Text);
            backupIntervalLoading = true;
            BackupIntervalBox.SelectedItem = BackupIntervalBox.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == profile.BackupIntervalHours.ToString()) ?? BackupIntervalBox.Items[0];
            backupIntervalLoading = false;
            BackupList.ItemsSource = System.IO.Directory.Exists(Path.Combine(profile.Directory, "backups"))
                ? System.IO.Directory.GetFiles(Path.Combine(profile.Directory, "backups"), "*.zip").OrderByDescending(x => x).Select(Path.GetFileName).ToList() : [];
            BackupProgressText.Text = busyBackups.Contains(profile.Id) ? T("BackupOperation") : "";
            LoadAllowList(profile);
            lastPlayerRefreshUtc = DateTime.MinValue;
            RememberEditor();
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } profile) Run(() => manager.Start(profile));
    }
    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } profile) { ShowBusy(T("Stopping")); await RunAsync(() => manager.Stop(profile)); }
    }
    private async void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } profile) { ShowBusy(T("WaitingRestart")); await RunAsync(async () => { await manager.Stop(profile); manager.Start(profile); }); }
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
    private void RefreshPlayersIfNeeded()
    {
        if (Pages.SelectedItem != PlayersTab || Selected is not { } profile || !manager.IsRunning(profile)) return;
        if (DateTime.UtcNow - lastPlayerRefreshUtc < TimeSpan.FromSeconds(10)) return;
        RequestPlayers(profile);
    }
    private void RequestPlayers(ServerProfile profile)
    {
        try { manager.RequestPlayers(profile); lastPlayerRefreshUtc = DateTime.UtcNow; }
        catch (Exception ex) { ShowNotice(ex.Message); }
    }
    private void RefreshOnlinePlayers(ServerProfile profile)
    {
        var names = manager.OnlinePlayers(profile);
        var selected = OnlinePlayersList.SelectedItem as string;
        if (OnlinePlayersList.ItemsSource is not IReadOnlyList<string> old || !old.SequenceEqual(names))
        {
            OnlinePlayersList.ItemsSource = names.ToArray();
            OnlinePlayersList.SelectedItem = selected;
        }
        OnlineStatusText.Text = manager.IsRunning(profile) ? F("OnlineCount", names.Count) : T("StartForOnlinePlayers");
    }
    private void RefreshPlayersButton_Click(object sender, RoutedEventArgs e) { if (Selected is { } profile) RequestPlayers(profile); }
    private void OnlinePlayersList_SelectionChanged(object sender, SelectionChangedEventArgs e) => Refresh();
    private void PlayerCommand(string verb, bool javaOnly = false)
    {
        if (Selected is not { } profile || OnlinePlayersList.SelectedItem is not string name) return;
        Run(() =>
        {
            if (javaOnly && profile.Edition != ServerEdition.Java) return;
            manager.Send(profile, verb + " " + AllowListStore.CommandName(profile, name));
            RequestPlayers(profile);
        });
    }
    private void KickPlayerButton_Click(object sender, RoutedEventArgs e) => PlayerCommand("kick");
    private void OpPlayerButton_Click(object sender, RoutedEventArgs e) => PlayerCommand("op");
    private void DeopPlayerButton_Click(object sender, RoutedEventArgs e) => PlayerCommand("deop");
    private async void BanPlayerButton_Click(object sender, RoutedEventArgs e)
    {
        if (OnlinePlayersList.SelectedItem is not string name || !await ConfirmAsync(T("BanPlayer"), F("BanConfirm", name))) return;
        PlayerCommand("ban", true);
    }
    private void UnbanPlayerButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { Edition: ServerEdition.Java } profile || string.IsNullOrWhiteSpace(PardonNameBox.Text)) return;
        Run(() => { manager.Send(profile, "pardon " + AllowListStore.CommandName(profile, PardonNameBox.Text)); PardonNameBox.Clear(); });
    }
    private void AllowOnlinePlayerButton_Click(object sender, RoutedEventArgs e)
    {
        if (OnlinePlayersList.SelectedItem is not string name) return;
        ChangeAllowList(name, true);
    }
    private void SetPlayerGameModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (PlayerGameModeBox.SelectedItem is not ComboBoxItem item) return;
        PlayerCommand("gamemode " + item.Tag);
    }
    private void MessagePlayerButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } profile || OnlinePlayersList.SelectedItem is not string name || string.IsNullOrWhiteSpace(PlayerMessageBox.Text)) return;
        Run(() =>
        {
            var message = PlayerMessageBox.Text.Trim();
            if (message.Contains('\n') || message.Contains('\r')) throw new InvalidOperationException(T("InvalidPlayerMessage"));
            manager.Send(profile, "tell " + AllowListStore.CommandName(profile, name) + " " + message);
            PlayerMessageBox.Clear();
        });
    }
    private void LoadAllowList(ServerProfile profile)
    {
        allowListLoading = true;
        try
        {
            AllowListEnabledBox.IsChecked = PropertiesFile.Get(profile, profile.Edition == ServerEdition.Java ? "white-list" : "allow-list", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
            var selected = AllowListNames.SelectedItem as string;
            AllowListNames.ItemsSource = AllowListStore.Read(profile);
            AllowListNames.SelectedItem = selected;
        }
        catch (Exception ex) { ShowNotice(ex.Message); }
        finally { allowListLoading = false; }
    }
    private void AllowListEnabledBox_Click(object sender, RoutedEventArgs e)
    {
        if (allowListLoading || Selected is not { } profile) return;
        Run(() =>
        {
            if (propertiesSnapshot != PropertiesBox.Text || SimpleEditor.IsDirty) throw new InvalidOperationException(T("SavePropertiesFirst"));
            var enabled = AllowListEnabledBox.IsChecked == true;
            PropertiesFile.Set(profile, profile.Edition == ServerEdition.Java ? "white-list" : "allow-list", enabled ? "true" : "false");
            PropertiesBox.Text = PropertiesFile.Read(profile);
            SimpleEditor.Load(profile.Edition, PropertiesBox.Text);
            propertiesSnapshot = PropertiesBox.Text;
            if (manager.IsRunning(profile)) manager.Send(profile, (profile.Edition == ServerEdition.Java ? "whitelist " : "allowlist ") + (enabled ? "on" : "off"));
        });
        LoadAllowList(profile);
    }
    private void ChangeAllowList(string name, bool add)
    {
        if (Selected is not { } profile) return;
        Run(() =>
        {
            var safe = AllowListStore.CommandName(profile, name);
            if (manager.IsRunning(profile)) manager.Send(profile, (profile.Edition == ServerEdition.Java ? "whitelist " : "allowlist ") + (add ? "add " : "remove ") + safe);
            else AllowListStore.ChangeOffline(profile, name.Trim(), add);
        });
        _ = RefreshAllowListLater(profile);
    }
    private async Task RefreshAllowListLater(ServerProfile profile)
    {
        await Task.Delay(600);
        if (Selected?.Id == profile.Id) LoadAllowList(profile);
    }
    private void AddAllowListButton_Click(object sender, RoutedEventArgs e)
    {
        var name = AllowListNameBox.Text.Trim();
        if (name.Length == 0) return;
        ChangeAllowList(name, true);
        AllowListNameBox.Clear();
    }
    private void RemoveAllowListButton_Click(object sender, RoutedEventArgs e)
    {
        if (AllowListNames.SelectedItem is string name) ChangeAllowList(name, false);
    }
    private void RefreshAllowListButton_Click(object sender, RoutedEventArgs e) { if (Selected is { } profile) LoadAllowList(profile); }
    private void AllowListNames_SelectionChanged(object sender, SelectionChangedEventArgs e) => Refresh();
    private void ConsoleSearchBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateConsoleSearch();
    private void UpdateConsoleSearch()
    {
        if (ConsoleText == null || SearchCountText == null || ConsoleSearchBox == null) return;
        var query = ConsoleSearchBox.Text;
        if (string.IsNullOrWhiteSpace(query)) { SearchCountText.Text = ""; return; }
        var log = ConsoleText.Text;
        var count = 0;
        var first = -1;
        for (var index = 0; (index = log.IndexOf(query, index, StringComparison.OrdinalIgnoreCase)) >= 0; index += query.Length)
        {
            if (first < 0) first = index;
            count++;
        }
        SearchCountText.Text = F("SearchMatches", count);
        if (first >= 0)
        {
            ConsoleText.Select(first, query.Length);
            ConsoleText.ScrollToLine(ConsoleText.GetLineIndexFromCharacterIndex(first));
        }
    }
    private void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } profile) return;
        Run(() =>
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text)) throw new InvalidOperationException(T("EnterName"));
            if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535) throw new InvalidOperationException(T("InvalidPort"));
            if (store.Profiles.Any(p => p.Id != profile.Id && p.Edition == profile.Edition && p.Port == port)) throw new InvalidOperationException(T("PortUsedProfile"));
            var memory = profile.MemoryMb;
            if (profile.Edition == ServerEdition.Java && (!int.TryParse(MemoryBox.Text, out memory) || memory is < 512 or > 131072)) throw new InvalidOperationException(T("JavaMemoryRange"));
            if (manager.IsRunning(profile) && profile.Port != port) throw new InvalidOperationException(T("StopBeforePortChange"));
            if (propertiesSnapshot != PropertiesBox.Text || SimpleEditor.IsDirty) throw new InvalidOperationException(T("SavePropertiesFirst"));
            var previous = (profile.Name, profile.Port, profile.MemoryMb, profile.JavaPath, profile.AutoRestart);
            var oldProperties = PropertiesFile.Read(profile);
            try
            {
                profile.Name = NameBox.Text.Trim(); profile.Port = port; profile.MemoryMb = memory;
                if (profile.Edition == ServerEdition.Java) profile.JavaPath = string.IsNullOrWhiteSpace(JavaBox.Text) ? "java" : JavaBox.Text.Trim();
                profile.AutoRestart = AutoRestartBox.IsChecked == true;
                PropertiesFile.Set(profile, "server-port", port.ToString());
                store.Save();
                NameBox.Text = profile.Name;
                PortBox.Text = profile.Port.ToString();
                MemoryBox.Text = profile.MemoryMb.ToString();
                JavaBox.Text = profile.JavaPath;
                PropertiesBox.Text = PropertiesFile.Read(profile);
                SimpleEditor.Load(profile.Edition, PropertiesBox.Text);
                propertiesSnapshot = PropertiesBox.Text;
                ServerList.Items.Refresh();
                profileSnapshot = CurrentProfileSnapshot();
            }
            catch
            {
                (profile.Name, profile.Port, profile.MemoryMb, profile.JavaPath, profile.AutoRestart) = previous;
                try { PropertiesFile.Write(profile, oldProperties); } catch { }
                throw;
            }
        });
    }
    private void SavePropertiesButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } profile) Run(() =>
        {
            var updated = SimpleEditor.ApplyChanges(PropertiesBox.Text);
            var configuredPort = PropertiesFile.GetValue(updated, "server-port", profile.Port.ToString());
            if (!int.TryParse(configuredPort, out var parsedPort) || parsedPort != profile.Port)
                throw new InvalidOperationException(T("PropertiesPortMismatch"));
            PropertiesFile.Write(profile, updated);
            PropertiesBox.Text = updated;
            SimpleEditor.Load(profile.Edition, updated);
            propertiesSnapshot = PropertiesBox.Text;
        });
    }
    private async void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } profile) await CreateBackupAsync(profile);
    }
    private async Task<bool> CreateBackupAsync(ServerProfile profile)
    {
        if (!busyBackups.Add(profile.Id)) return false;
        if (Selected?.Id == profile.Id) { BackupProgressText.Text = T("CreatingBackup"); ShowBusy(T("CreatingBackup")); }
        Refresh();
        try
        {
            var path = await Task.Run(() => manager.CreateBackup(profile));
            var previousBackupUtc = profile.LastBackupUtc;
            profile.LastBackupUtc = DateTime.UtcNow;
            try { store.Save(); }
            catch { profile.LastBackupUtc = previousBackupUtc; throw; }
            if (Selected?.Id == profile.Id)
            {
                RefreshBackupList(profile);
                ShowBackupNotice(F("BackupCreated", path), path);
            }
            return true;
        }
        catch (Exception ex) { ShowNotice(F("BackupFailed", profile.Name, ex.Message)); return false; }
        finally
        {
            busyBackups.Remove(profile.Id);
            if (Selected?.Id == profile.Id) BackupProgressText.Text = "";
            Refresh();
        }
    }
    private void RefreshBackupList(ServerProfile profile)
    {
        var folder = Path.Combine(profile.Directory, "backups");
        BackupList.ItemsSource = System.IO.Directory.Exists(folder)
            ? System.IO.Directory.GetFiles(folder, "*.zip").OrderByDescending(x => x).Select(Path.GetFileName).ToList() : [];
    }
    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } profile || BackupList.SelectedItem is not string fileName) return;
        if (manager.IsRunning(profile)) { ShowNotice(T("StopBeforeRestore")); return; }
        if (!await ConfirmAsync(T("RestoreTitle"), T("RestoreConfirm"))) return;
        if (!busyBackups.Add(profile.Id)) return;
        BackupProgressText.Text = T("RestoringWorld");
        ShowBusy(T("RestoringWorld"));
        Refresh();
        try
        {
            await Task.Run(() => manager.RestoreBackup(profile, Path.Combine(profile.Directory, "backups", fileName)));
            ShowNotice(F("WorldRestored", fileName));
        }
        catch (Exception ex) { ShowNotice(ex.Message); }
        finally { busyBackups.Remove(profile.Id); BackupProgressText.Text = ""; Refresh(); }
    }
    private void BackupList_SelectionChanged(object sender, SelectionChangedEventArgs e) => Refresh();
    private static void ShowFileInExplorer(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(T("BackupNotFound"), path);
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", path }, UseShellExecute = true });
    }
    private void OpenBackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } profile && BackupList.SelectedItem is string name)
            try { ShowFileInExplorer(Path.Combine(profile.Directory, "backups", name)); }
            catch (Exception ex) { ShowNotice(ex.Message); }
    }
    private void NoticeOpenBackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (noticeBackupPath is { } path)
            try { ShowFileInExplorer(path); }
            catch (Exception ex) { ShowNotice(ex.Message); }
    }
    private void BackupIntervalBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (backupIntervalLoading || Selected is not { } profile || BackupIntervalBox.SelectedItem is not ComboBoxItem item) return;
        var previous = profile.BackupIntervalHours;
        profile.BackupIntervalHours = int.Parse((string)item.Tag);
        try { store.Save(); }
        catch (Exception ex) { profile.BackupIntervalHours = previous; ShowNotice(ex.Message); }
    }
    private async Task RunScheduledBackups()
    {
        foreach (var profile in store.Profiles.ToList())
        {
            if (closing) return;
            if (profile.BackupIntervalHours <= 0 || busyBackups.Contains(profile.Id) || manager.IsRunning(profile) || manager.State(profile) == ServerState.Restarting) continue;
            if (profile.LastBackupUtc is { } last && DateTime.UtcNow - last < TimeSpan.FromHours(profile.BackupIntervalHours)) continue;
            if (backupRetryAfter.TryGetValue(profile.Id, out var retryAfter) && DateTime.UtcNow < retryAfter) continue;
            if (await CreateBackupAsync(profile)) backupRetryAfter.Remove(profile.Id);
            else backupRetryAfter[profile.Id] = DateTime.UtcNow.AddHours(1);
        }
    }
    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } profile) return;
        try
        {
            if (!Directory.Exists(profile.Directory)) throw new DirectoryNotFoundException(profile.Directory);
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { profile.Directory }, UseShellExecute = true });
        }
        catch (Exception ex) { ShowNotice(ex.Message); }
    }
    private async void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } profile) return;
        if (deleting || busyBackups.Contains(profile.Id)) { ShowNotice(T("FinishBeforeDelete")); return; }
        if (manager.IsRunning(profile) || manager.State(profile) is ServerState.Starting or ServerState.Stopping) { ShowNotice(T("StopBeforeDelete")); return; }
        try { ServerFolderDeletion.Validate(profile, store.Profiles); }
        catch (Exception ex) { ShowNotice(ex.Message); return; }
        if (EditorDirty() && !await ConfirmAsync(T("UnsavedTitle"), T("DeleteDiscard"))) return;
        if (!await ConfirmAsync(T("DeleteServer"), F("DeleteConfirm", profile.Directory))) return;
        if (busyBackups.Contains(profile.Id) || manager.IsRunning(profile) || manager.State(profile) is ServerState.Starting or ServerState.Stopping)
        {
            ShowNotice(T("FinishBeforeDelete"));
            return;
        }
        try { ServerFolderDeletion.Validate(profile, store.Profiles); }
        catch (Exception ex) { ShowNotice(ex.Message); return; }
        deleting = true;
        IsEnabled = false;
        ShowBusy(T("DeletingServer"));
        var profiles = store.Profiles.ToList();
        var index = store.Profiles.IndexOf(profile);
        try
        {
            manager.CancelPendingRestart(profile);
            revertingSelection = true;
            store.Profiles.Remove(profile);
            try { store.Save(); }
            catch { store.Profiles.Insert(index, profile); throw; }
            try { await Task.Run(() => ServerFolderDeletion.Delete(profile, profiles)); }
            catch
            {
                store.Profiles.Insert(index, profile);
                store.Save();
                throw;
            }
            editorProfile = null;
            ServerList.Items.Refresh();
            ServerList.SelectedIndex = store.Profiles.Count > 0 ? 0 : -1;
            ClearNotice();
        }
        catch (Exception ex)
        {
            ServerList.Items.Refresh();
            ServerList.SelectedItem = profile;
            ShowNotice(F("DeleteServerFailed", ex.Message));
        }
        finally
        {
            revertingSelection = false;
            deleting = false;
            IsEnabled = true;
            Refresh(true);
        }
    }
    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e) => await CheckForUpdates(true);
    private async Task CheckForUpdates(bool showError)
    {
        UpdateStatus.Text = T("CheckingUpdates");
        UpdateProgressBar.Visibility = Visibility.Visible;
        CheckUpdateButton.IsEnabled = false;
        try
        {
            var outcome = await updateChecker.Check();
            var result = outcome.Result;
            lastUpdateResult = result;
            updateUrl = result.Available ? result.Url : null;
            RenderUpdateStatus();
            if (showError && outcome.FromCache)
                ShowNotice(F("UpdateCheckCooldown", Math.Max(1, (int)Math.Ceiling((outcome.NextAllowedUtc - DateTime.UtcNow).TotalMinutes))));
            else if (result.Error != null && showError) ShowNotice("GitHub: " + result.Error);
        }
        finally { UpdateProgressBar.Visibility = Visibility.Collapsed; CheckUpdateButton.IsEnabled = true; }
    }
    private void RenderUpdateStatus()
    {
        var result = lastUpdateResult;
        UpdateStatus.Text = result == null ? T("CheckingUpdates") : result.Available ? F("NewVersion", result.Latest) : result.NoRelease ? T("NoReleases") : result.Error != null ? T("UpdateFailed") : T("UpToDate");
    }
    protected override async void OnClosing(CancelEventArgs e)
    {
        if (closing) { base.OnClosing(e); return; }
        if (!exitRequested)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        e.Cancel = true;
        if (InlineOverlay.Visibility == Visibility.Visible) { exitRequested = false; return; }
        if (EditorDirty() && !await ConfirmAsync(T("UnsavedTitle"), T("CloseDiscard"))) { exitRequested = false; return; }
        closing = true;
        IsEnabled = false;
        refreshTimer.Stop();
        backupTimer.Stop();
        if (installing || deleting || busyBackups.Count > 0)
        {
            ShowNotice(T("FinishingFileOperations"));
            while (installing || deleting || busyBackups.Count > 0) await Task.Delay(100);
        }
        try { await manager.StopAll(); Close(); }
        catch (Exception ex)
        {
            manager.ResumeAfterFailedShutdown();
            closing = false;
            exitRequested = false;
            IsEnabled = true;
            refreshTimer.Start();
            backupTimer.Start();
            ShowNotice(F("StopAllFailed", ex.Message));
        }
    }
    private void RestoreWindow()
    {
        Show();
        WindowState = WindowState.Maximized;
        Activate();
    }
    private void RequestExit()
    {
        RestoreWindow();
        exitRequested = true;
        Close();
    }
}
