using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace MinecraftServerManager;

public sealed class SimplePropertiesEditor : StackPanel
{
    private sealed record PropertySpec(string Key, string LabelKey, string DefaultValue, bool Boolean = false, string[]? Options = null);
    private sealed record Field(string Original, Func<string> Read);
    private readonly Dictionary<string, Field> fields = new(StringComparer.OrdinalIgnoreCase);

    private static readonly PropertySpec[] Java =
    [
        new("motd", "PropertyMotd", "A Minecraft Server"),
        new("gamemode", "PropertyGamemode", "survival", Options: ["survival", "creative", "adventure", "spectator"]),
        new("difficulty", "PropertyDifficulty", "easy", Options: ["peaceful", "easy", "normal", "hard"]),
        new("max-players", "PropertyMaxPlayers", "20"),
        new("online-mode", "PropertyOnlineMode", "true", true),
        new("pvp", "PropertyPvp", "true", true),
        new("view-distance", "PropertyViewDistance", "10"),
        new("simulation-distance", "PropertySimulationDistance", "10"),
        new("enable-command-block", "PropertyCommandBlocks", "false", true),
        new("allow-flight", "PropertyFlight", "false", true),
        new("level-name", "PropertyLevelName", "world"),
        new("spawn-protection", "PropertySpawnProtection", "16"),
        new("hardcore", "PropertyHardcore", "false", true)
    ];

    private static readonly PropertySpec[] Bedrock =
    [
        new("server-name", "PropertyMotd", "Dedicated Server"),
        new("gamemode", "PropertyGamemode", "survival", Options: ["survival", "creative", "adventure"]),
        new("difficulty", "PropertyDifficulty", "easy", Options: ["peaceful", "easy", "normal", "hard"]),
        new("max-players", "PropertyMaxPlayers", "10"),
        new("online-mode", "PropertyOnlineMode", "true", true),
        new("allow-cheats", "PropertyCheats", "false", true),
        new("view-distance", "PropertyViewDistance", "32"),
        new("tick-distance", "PropertyTickDistance", "4"),
        new("level-name", "PropertyLevelName", "Bedrock level"),
        new("default-player-permission-level", "PropertyPermissionLevel", "member", Options: ["visitor", "member", "operator"]),
        new("texturepack-required", "PropertyTexturePack", "false", true)
    ];

    public bool IsDirty => fields.Values.Any(entry => entry.Original != entry.Read());

    public void Load(ServerEdition edition, string content)
    {
        Children.Clear();
        fields.Clear();
        var entries = PropertiesFile.Entries(content);
        var known = edition == ServerEdition.Java ? Java : Bedrock;
        var otherEdition = edition == ServerEdition.Java ? Bedrock : Java;
        var editionOnlyKeys = otherEdition.Select(spec => spec.Key).Except(known.Select(spec => spec.Key), StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in known)
            Add(spec, entries.TryGetValue(spec.Key, out var value) ? value : spec.DefaultValue);
        foreach (var (key, value) in entries.OrderBy(x => x.Key))
        {
            if (key.Equals("server-port", StringComparison.OrdinalIgnoreCase) || key.Equals("white-list", StringComparison.OrdinalIgnoreCase) || key.Equals("allow-list", StringComparison.OrdinalIgnoreCase) || editionOnlyKeys.Contains(key) || fields.ContainsKey(key)) continue;
            Add(new PropertySpec(key, key, value, value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase)), value);
        }
    }

    public string ApplyChanges(string content)
    {
        foreach (var (key, field) in fields)
        {
            var value = field.Read();
            if (value != field.Original) content = PropertiesFile.SetValue(content, key, value);
        }
        return content;
    }

    private void Add(PropertySpec spec, string value)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var label = new TextBlock { ToolTip = spec.Key, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) };
        if (spec.LabelKey == spec.Key) label.Text = spec.Key;
        else label.SetBinding(TextBlock.TextProperty, new Binding($"[{spec.LabelKey}]") { Source = Localization.Instance });
        row.Children.Add(label);
        if (spec.Boolean)
        {
            var toggle = new CheckBox { IsChecked = value.Equals("true", StringComparison.OrdinalIgnoreCase), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);
            fields[spec.Key] = new Field(value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false", () => toggle.IsChecked == true ? "true" : "false");
        }
        else if (spec.Options is { } options)
        {
            var choice = new ComboBox { ToolTip = spec.Key };
            foreach (var option in options)
            {
                var item = new ComboBoxItem { Tag = option };
                item.SetBinding(ContentControl.ContentProperty, new Binding("[Option" + char.ToUpperInvariant(option[0]) + option[1..] + "]") { Source = Localization.Instance });
                choice.Items.Add(item);
            }
            choice.SelectedItem = choice.Items.Cast<ComboBoxItem>().FirstOrDefault(item => string.Equals((string)item.Tag, value, StringComparison.OrdinalIgnoreCase));
            if (choice.SelectedItem == null) choice.Items.Insert(0, new ComboBoxItem { Content = value, Tag = value, IsSelected = true });
            Grid.SetColumn(choice, 1);
            row.Children.Add(choice);
            fields[spec.Key] = new Field(value, () => (string)((ComboBoxItem)choice.SelectedItem!).Tag);
        }
        else
        {
            var input = new TextBox { Text = value, ToolTip = spec.Key };
            Grid.SetColumn(input, 1);
            row.Children.Add(input);
            fields[spec.Key] = new Field(value, () => input.Text.Trim());
        }
        Children.Add(row);
    }
}
