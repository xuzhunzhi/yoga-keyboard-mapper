namespace YogaKeyboardMapper.Core;

public sealed class ActionPreset
{
    public ActionPreset(string categoryKey, string id, string action)
    {
        CategoryKey = categoryKey;
        Id = id;
        Action = action;
    }

    /// <summary>Stable, non-localized category id ("System", "Window", ...).</summary>
    public string CategoryKey { get; }

    /// <summary>Stable, non-localized preset id ("TaskManager", ...).</summary>
    public string Id { get; }

    public string Action { get; }

    public string Category => Res.Get("PresetCategory_" + CategoryKey);

    public string Name => Res.Get("Preset_" + Id);

    // ComboBox items expose ToString() to screen readers.
    public override string ToString() => Name;
}

public sealed class PresetCategory
{
    public PresetCategory(string key)
    {
        Key = key;
    }

    /// <summary>Stable, non-localized category id ("System", "Window", ...).</summary>
    public string Key { get; }

    public string Name => Res.Get("PresetCategory_" + Key);

    public IEnumerable<ActionPreset> Presets => ActionPresets.All.Where(p => p.CategoryKey == Key);

    public override string ToString() => Name;
}

public static class ActionPresets
{
    /// <summary>
    /// Built-in actions, grouped by category. The editor's preset pickers show the
    /// categories and their actions in this order.
    /// </summary>
    public static readonly IReadOnlyList<ActionPreset> All = new List<ActionPreset>
    {
        new("System", "TaskManager", "ctrl+shift+esc"),
        new("System", "Lock", "win+l"),
        new("System", "Screenshot", "win+shift+s"),
        new("System", "Settings", "win+i"),
        new("System", "FileExplorer", "win+e"),
        new("System", "ShowDesktop", "win+d"),
        new("System", "TaskView", "win+tab"),
        new("System", "RunDialog", "win+r"),
        new("System", "ClipboardHistory", "win+v"),
        new("System", "SystemInformation", "run:msinfo32"),
        new("System", "ControlPanel", "run:control"),
        new("System", "DeviceManager", "run:devmgmt.msc"),
        new("System", "Services", "run:services.msc"),
        new("System", "DiskManagement", "run:diskmgmt.msc"),
        new("System", "RegistryEditor", "run:regedit"),

        new("Window", "SwitchWindows", "alt+tab"),
        new("Window", "SwitchWindowsReverse", "alt+shift+tab"),
        new("Window", "CloseWindow", "alt+f4"),
        new("Window", "Maximize", "win+up"),
        new("Window", "Minimize", "win+down"),
        new("Window", "SnapLeft", "win+left"),
        new("Window", "SnapRight", "win+right"),

        new("Edit", "Copy", "ctrl+c"),
        new("Edit", "Paste", "ctrl+v"),
        new("Edit", "Cut", "ctrl+x"),
        new("Edit", "Undo", "ctrl+z"),
        new("Edit", "Redo", "ctrl+y"),
        new("Edit", "SelectAll", "ctrl+a"),
        new("Edit", "Save", "ctrl+s"),
        new("Edit", "Find", "ctrl+f"),
        new("Edit", "New", "ctrl+n"),

        new("Apps", "Calculator", "run:calc"),
        new("Apps", "Notepad", "run:notepad"),
        new("Apps", "Paint", "run:mspaint"),
        new("Apps", "CommandPrompt", "run:cmd"),
        new("Apps", "PowerShell", "run:powershell"),
        new("Apps", "Terminal", "run:wt"),
        new("Apps", "WordPad", "run:write"),
        new("Apps", "Magnifier", "run:Magnify"),

        new("Files", "DownloadsFolder", "run:explorer.exe shell:Downloads"),
        new("Files", "DesktopFolder", "run:explorer.exe shell:Desktop"),
        new("Files", "DocumentsFolder", "run:explorer.exe shell:Personal"),
        new("Files", "RecycleBin", "run:explorer.exe shell:RecycleBinFolder"),

        new("Media", "PlayPause", "mediaplaypause"),
        new("Media", "NextTrack", "medianext"),
        new("Media", "PreviousTrack", "mediaprev"),
        new("Media", "VolumeUp", "volumeup"),
        new("Media", "VolumeDown", "volumedown"),
        new("Media", "Mute", "volumemute"),
    };

    /// <summary>Categories in the order they first appear in <see cref="All"/>.</summary>
    public static readonly IReadOnlyList<PresetCategory> Categories =
        All.Select(p => p.CategoryKey).Distinct().Select(key => new PresetCategory(key)).ToList();

    public static string Describe(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return Res.Get("Describe_NotSet");
        }

        foreach (ActionPreset preset in All)
        {
            if (string.Equals(preset.Action, action, StringComparison.OrdinalIgnoreCase))
            {
                return Res.Format("Describe_PresetFormat", preset.Name, action);
            }
        }

        if (action.StartsWith("run:", StringComparison.OrdinalIgnoreCase))
        {
            return Res.Format("Describe_RunFormat", action[4..].Trim());
        }

        if (action.StartsWith("url:", StringComparison.OrdinalIgnoreCase))
        {
            return Res.Format("Describe_UrlFormat", action[4..].Trim());
        }

        return action;
    }
}
