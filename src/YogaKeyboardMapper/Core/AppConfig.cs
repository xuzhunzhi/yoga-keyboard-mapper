using System.Text.Json;

namespace YogaKeyboardMapper.Core;

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

    /// <summary>Whether the raw-input listener should be active after a restart.</summary>
    public bool ListenOnStartup { get; set; } = true;

    public List<KeyMapping> Mappings { get; set; } = new();

    /// <summary>
    /// Data folder. The MSIX build uses its package's LocalState folder: writes to
    /// %LOCALAPPDATA% would be redirected into a hidden per-package copy, so "open folder"
    /// would show stale files.
    /// </summary>
    public static string Folder { get; } = AppPackage.IsPackaged
        ? Windows.Storage.ApplicationData.Current.LocalFolder.Path
        : UnpackagedFolder;

    public static string FilePath => Path.Combine(Folder, "config.json");

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>Data folder of the zip / setup.exe builds.</summary>
    private static string UnpackagedFolder => Path.Combine(LocalAppData, "YogaKeyboardMapper");

    /// <summary>
    /// Configs to import on first run, newest first: the unpackaged build's (when running
    /// from the MSIX), then the one written under the old name "YogaKey".
    /// </summary>
    private static IEnumerable<string> MigrationSources()
    {
        if (AppPackage.IsPackaged)
        {
            yield return Path.Combine(UnpackagedFolder, "config.json");
        }

        yield return Path.Combine(LocalAppData, "YogaKey", "config.json");
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Loads the config. If the file exists but cannot be parsed it is moved aside
    /// (config.json.bad-yyyyMMdd-HHmmss) before falling back to defaults, so the next
    /// Save() can never silently overwrite the user's mappings. <paramref name="error"/>
    /// describes what happened in that case.
    /// </summary>
    public static AppConfig Load(out string? error)
    {
        error = null;
        MigrateLegacyFile();
        if (!File.Exists(FilePath))
        {
            return new AppConfig();
        }

        try
        {
            string json = File.ReadAllText(FilePath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, Options);
            if (config is not null)
            {
                config.Mappings ??= new();
                config.DeviceMatch ??= string.Empty;
                config.CollectionMatch ??= string.Empty;
                return config;
            }

            error = Res.Get("Config_Empty");
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        try
        {
            string backup = $"{FilePath}.bad-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(FilePath, backup);
            error = Res.Format("Config_BackedUpFormat", error, Path.GetFileName(backup));
        }
        catch (Exception ex)
        {
            error = Res.Format("Config_BackupFailedFormat", error, ex.Message);
        }

        return new AppConfig();
    }

    /// <summary>
    /// Copies an existing config (see <see cref="MigrationSources"/>) on first run. The
    /// source is left in place, so the other build still finds its mappings.
    /// </summary>
    private static void MigrateLegacyFile()
    {
        if (File.Exists(FilePath) || MigrationSources().FirstOrDefault(File.Exists) is not string source)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Folder);
            File.Copy(source, FilePath);
            Log.Write("migrated config from " + source);
        }
        catch (Exception ex)
        {
            Log.Write("config migration failed: " + ex.Message);
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);

        // Write to a temp file and swap it in, so a crash mid-write cannot leave a
        // truncated config.json behind.
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}
