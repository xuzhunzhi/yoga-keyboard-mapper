using Microsoft.Win32;

using Windows.ApplicationModel;

namespace YogaKeyboardMapper.Core;

/// <summary>
/// Start with Windows. Unpackaged builds use an HKCU Run entry; the MSIX build uses the
/// startup task declared in Package.appxmanifest, because a packaged app's HKCU writes
/// are redirected and Windows never sees them.
/// </summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "YogaKeyboardMapper";

    /// <summary>Run entry written by versions released under the old name "YogaKey".</summary>
    private const string LegacyValueName = "YogaKey";

    /// <summary>TaskId of the windows.startupTask extension in Package.appxmanifest.</summary>
    private const string StartupTaskId = "YogaKeyboardMapperStartup";

    /// <summary>Command-line switch used by the Run entry so login starts the window minimised.</summary>
    public const string MinimizedArg = "--minimized";

    private static string ExpectedCommand =>
        Environment.ProcessPath is { Length: > 0 } exe ? $"\"{exe}\" {MinimizedArg}" : string.Empty;

    /// <summary>True only if the Run entry points at this executable (or the startup task is on).</summary>
    public static bool IsEnabled()
    {
        if (AppPackage.IsPackaged)
        {
            return GetStartupTask() is { } task && IsOn(task.State);
        }

        string? exe = Environment.ProcessPath;
        return !string.IsNullOrEmpty(exe) &&
               ReadCommand() is string value &&
               value.Contains(exe, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Replaces a Run entry left by the old "YogaKey" name with one for this exe, and
    /// rewrites an existing entry that points at this exe but uses an older command
    /// line (e.g. one written before <see cref="MinimizedArg"/> existed).
    /// </summary>
    public static void Refresh()
    {
        if (AppPackage.IsPackaged)
        {
            return;
        }

        using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, true))
        {
            if (key?.GetValue(LegacyValueName) is not null)
            {
                key.DeleteValue(LegacyValueName, false);
                SetRunEntry(true);
            }
        }

        string expected = ExpectedCommand;
        if (expected.Length > 0 && IsEnabled() &&
            !string.Equals(ReadCommand(), expected, StringComparison.OrdinalIgnoreCase))
        {
            SetRunEntry(true);
        }
    }

    /// <summary>
    /// Turns autostart on or off and returns whether it is on afterwards. Enabling the
    /// startup task fails if the user switched it off in Settings / Task Manager, which
    /// only they can undo.
    /// </summary>
    public static async Task<bool> SetAsync(bool enabled)
    {
        if (!AppPackage.IsPackaged)
        {
            SetRunEntry(enabled);
            return IsEnabled();
        }

        StartupTask task = await StartupTask.GetAsync(StartupTaskId);
        if (!enabled)
        {
            task.Disable();
            return false;
        }

        return IsOn(task.State) || IsOn(await task.RequestEnableAsync());
    }

    private static StartupTask? GetStartupTask()
    {
        try
        {
            return StartupTask.GetAsync(StartupTaskId).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Write("startup task error: " + ex.Message);
            return null;
        }
    }

    private static bool IsOn(StartupTaskState state) =>
        state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

    private static void SetRunEntry(bool enabled)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            string command = ExpectedCommand;
            if (command.Length > 0)
            {
                key.SetValue(ValueName, command);
            }
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }

    private static string? ReadCommand()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) as string;
    }
}
