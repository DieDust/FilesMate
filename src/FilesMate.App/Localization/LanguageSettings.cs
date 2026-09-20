using System.Globalization;
using System.Text.Json;

namespace FilesMate.App.Localization;

/// <summary>Shared by the file manager and the independently running search host.</summary>
public static class LanguageSettings
{
    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FilesMate", "language.json");

    public static string Normalize(string? language) => language switch
    {
        "en-US" or "zh-CN" or "ja-JP" => language,
        _ => "System",
    };

    public static string Load(string path)
    {
        try { return Normalize(JsonSerializer.Deserialize<Preference>(File.ReadAllText(path))?.Language); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return "System"; }
    }

    public static void Save(string path, string language)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Preference(Normalize(language))));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void Apply(string path)
    {
        var language = Load(path);
        if (language != "System")
        {
            var culture = CultureInfo.GetCultureInfo(language);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        // Keep CurrentCulture unchanged: dates and numbers retain the user's regional format.
        StringTable.UseUiCulture = true;
    }

    private sealed record Preference(string Language);
}
