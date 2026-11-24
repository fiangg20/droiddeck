using System.Text.Json;

namespace Droideck.Core;

public class AppSettings
{
    public List<string> Saved { get; set; } = new();
    public string AdbPath { get; set; } = "";
    public bool SkipSetup { get; set; }
}

public static class Store
{
    static string FilePath => Path.Combine(Util.AppData, "settings.json");

    public static AppSettings Settings { get; private set; } = Load();

    static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
        }
        return new AppSettings();
    }

    public static void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }
}
