using System.Text.Json;

namespace YogaKey.Core;

public sealed class KeyMapping
{
    public int Code { get; set; }
    public string Action { get; set; } = string.Empty;
}

public sealed class AppConfig
{
    /// <summary>
    /// Substring of the raw-input device path that identifies the keyboard.
    /// "FE5A42B74943" is the BLE instance id of a Yoga Book 9 keyboard.
    /// Leave empty to accept every device.
    /// </summary>
    public string DeviceMatch { get; set; } = "FE5A42B74943";

    /// <summary>Substring that identifies the HID collection ("Col05").</summary>
    public string CollectionMatch { get; set; } = "Col05";

    public bool StartWithWindows { get; set; }

    public List<KeyMapping> Mappings { get; set; } = new();

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YogaKey");

    public static string FilePath => Path.Combine(Folder, "config.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, Options);
                if (config is not null)
                {
                    return config;
                }
            }
        }
        catch
        {
            // corrupted file -> fall back to defaults
        }

        return new AppConfig();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
    }
}
