using Microsoft.Win32;

namespace YogaKey.Core;

public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "YogaKey";

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    public static void Set(bool enabled)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            string exe = Environment.ProcessPath ?? string.Empty;
            if (exe.Length > 0)
            {
                key.SetValue(ValueName, $"\"{exe}\"");
            }
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }
}
