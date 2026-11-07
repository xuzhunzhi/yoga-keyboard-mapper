using System.Runtime.InteropServices;

namespace YogaKeyboardMapper.Core;

/// <summary>
/// Whether the app runs with package identity (installed from the MSIX / Microsoft Store)
/// or unpackaged (zip / setup.exe). Packaged apps get AppData and HKCU writes redirected
/// into a per-package store, so some features take a different path.
/// </summary>
public static class AppPackage
{
    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    public static bool IsPackaged { get; } = Detect();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);

    private static bool Detect()
    {
        int length = 0;
        return GetCurrentPackageFullName(ref length, null) != APPMODEL_ERROR_NO_PACKAGE;
    }
}
