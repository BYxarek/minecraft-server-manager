using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;

namespace MinecraftServerManager;

// A file/folder browser embedded in the current page; it never opens a native dialog.
public sealed class InlinePathBrowser : Border
{
    private readonly TextBox location = new();
    private readonly ListBox entries = new() { Height = 170 };
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button choose;
    private Action<string>? selected;
    private string? extension;
    private bool foldersOnly;

    public InlinePathBrowser()
    {
        Background = (System.Windows.Media.Brush)Application.Current.Resources["SideBrush"];
        BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["LineBrush"];
        BorderThickness = new Thickness(1);
        Padding = new Thickness(12);
        Margin = new Thickness(0, 10, 0, 10);
        Visibility = Visibility.Collapsed;
        var content = new StackPanel();
        Child = content;
        var controls = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var up = new Button { Content = IconContent.Create(PackIconKind.ArrowUp, T("BrowserUp")) };
        up.Click += (_, _) => Navigate(Directory.GetParent(location.Text)?.FullName);
        DockPanel.SetDock(up, Dock.Right);
        controls.Children.Add(up);
        var go = new Button { Content = IconContent.Create(PackIconKind.ArrowRight, T("BrowserGo")) };
        go.Click += (_, _) => Navigate(location.Text);
        DockPanel.SetDock(go, Dock.Right);
        controls.Children.Add(go);
        controls.Children.Add(location);
        content.Children.Add(controls);
        entries.MouseDoubleClick += (_, _) => OpenSelected();
        content.Children.Add(entries);
        error.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["MutedBrush"];
        content.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var open = new Button { Content = IconContent.Create(PackIconKind.FolderOpen, T("BrowserOpen")) };
        open.Click += (_, _) => OpenSelected();
        actions.Children.Add(open);
        choose = new Button { Content = IconContent.Create(PackIconKind.Check, T("BrowserSelectFolder")) };
        choose.Click += (_, _) => ChooseFolder();
        actions.Children.Add(choose);
        var cancel = new Button { Content = IconContent.Create(PackIconKind.Close, T("Cancel")) };
        cancel.Click += (_, _) => Visibility = Visibility.Collapsed;
        actions.Children.Add(cancel);
        content.Children.Add(actions);
    }

    public void Show(string? initialPath, bool selectFolder, string? fileExtension, Action<string> onSelected)
    {
        foldersOnly = selectFolder;
        choose.Visibility = selectFolder ? Visibility.Visible : Visibility.Collapsed;
        extension = fileExtension;
        selected = onSelected;
        Visibility = Visibility.Visible;
        var initial = initialPath;
        if (string.IsNullOrWhiteSpace(initial)) initial = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (File.Exists(initial)) initial = Path.GetDirectoryName(initial);
        while (!string.IsNullOrWhiteSpace(initial) && !Directory.Exists(initial)) initial = Path.GetDirectoryName(initial);
        Navigate(initial);
    }

    private void Navigate(string? path)
    {
        try
        {
            error.Text = "";
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) throw new DirectoryNotFoundException(path);
            location.Text = Path.GetFullPath(path);
            var directories = Directory.EnumerateDirectories(location.Text).Select(x => new Entry(x, true));
            var files = foldersOnly ? Enumerable.Empty<Entry>() : Directory.EnumerateFiles(location.Text)
                .Where(x => extension == null || x.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                .Select(x => new Entry(x, false));
            entries.ItemsSource = directories.Concat(files).OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name).ToList();
        }
        catch (Exception ex) { error.Text = ex.Message; }
    }

    private void OpenSelected()
    {
        if (entries.SelectedItem is not Entry entry) return;
        if (entry.IsDirectory) Navigate(entry.Path);
        else { selected?.Invoke(entry.Path); Visibility = Visibility.Collapsed; }
    }

    private void ChooseFolder()
    {
        if (!Directory.Exists(location.Text)) { error.Text = T("ChooseServerFolder"); return; }
        selected?.Invoke(location.Text);
        Visibility = Visibility.Collapsed;
    }

    private sealed record Entry(string Path, bool IsDirectory)
    {
        public string Name => (IsDirectory ? "▸ " : "") + System.IO.Path.GetFileName(Path);
        public override string ToString() => Name;
    }
}
