using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Markup;

namespace MinecraftServerManager;

public sealed class Localization : INotifyPropertyChanged
{
    public static Localization Instance { get; } = new();
    private readonly Dictionary<string, string> english = Load("en");
    private readonly Dictionary<string, string> russian = Load("ru");
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftServerManager", "settings.json");
    public string CurrentLanguage { get; private set; } = "en";
    public event PropertyChangedEventHandler? PropertyChanged;

    private Localization()
    {
        try
        {
            if (File.Exists(settingsPath))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(settingsPath));
                if (json.RootElement.TryGetProperty("language", out var language) && language.GetString() == "ru") CurrentLanguage = "ru";
            }
        }
        catch (Exception) { CurrentLanguage = "en"; }
    }

    public string this[string key] => (CurrentLanguage == "ru" ? russian : english).GetValueOrDefault(key)
        ?? english.GetValueOrDefault(key) ?? key;

    public static string T(string key) => Instance[key];
    public static string F(string key, params object?[] values) =>
        string.Format(CultureInfo.GetCultureInfo(Instance.CurrentLanguage == "ru" ? "ru-RU" : "en-US"), T(key), values);

    public void SetLanguage(string language)
    {
        if (language is not ("en" or "ru")) throw new ArgumentOutOfRangeException(nameof(language));
        if (CurrentLanguage == language) return;
        var directory = Path.GetDirectoryName(settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporary = settingsPath + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { language }));
            File.Move(temporary, settingsPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        CurrentLanguage = language;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private static Dictionary<string, string> Load(string language)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"MinecraftServerManager.Resources.{language}.json")
            ?? throw new InvalidOperationException($"Missing {language} translations.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? throw new InvalidDataException($"Invalid {language} translations.");
    }
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{key}]") { Source = Localization.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
