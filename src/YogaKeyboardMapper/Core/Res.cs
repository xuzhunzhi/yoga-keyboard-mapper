using System.Globalization;

using Microsoft.Windows.ApplicationModel.Resources;

namespace YogaKeyboardMapper.Core;

/// <summary>
/// Localized strings from Strings\&lt;language&gt;\Resources.resw. Uses the MRT Core loader,
/// which (unlike Windows.ApplicationModel.Resources) also works for unpackaged apps.
/// </summary>
public static class Res
{
    private static readonly ResourceLoader Loader = new();

    public static string Get(string key)
    {
        string value = Loader.GetString(key);
        return string.IsNullOrEmpty(value) ? key : value;
    }

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
