using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;

namespace MinecraftServerManager;

public static class IconContent
{
    public static StackPanel Create(PackIconKind icon, string label)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new PackIcon { Kind = icon, Style = (Style)Application.Current.Resources["UiIcon"] });
        content.Children.Add(new TextBlock { Text = label });
        return content;
    }
}
