using System.Runtime.InteropServices;

namespace YogaKeyboardMapper.Core;

/// <summary>
/// Notification-area icon for the "close to tray" mode. The message-only window is created
/// on the calling (UI) thread, so its messages are dispatched by the XAML message loop.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint WM_APP = 0x8000;
    private const uint TrayCallback = WM_APP + 1;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;
    private const int IDM_SHOW = 1;
    private const int IDM_EXIT = 2;
    private const int WM_NULL = 0x0000;

    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;
    private const uint NIF_INFO = 0x10;
    private const uint NIIF_INFO = 0x01;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x10;
    private const uint LR_DEFAULTSIZE = 0x40;
    private const uint MF_STRING = 0x00;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const int ERROR_CLASS_ALREADY_EXISTS = 1410;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string file, int index, out IntPtr large, out IntPtr small, uint count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadImageW(IntPtr instance, string name, uint type, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, IntPtr id, string text);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hWnd, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? module);

    private static readonly IntPtr HWND_MESSAGE = new(-3);
    private static readonly uint TaskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
    private static readonly int DataSize = Marshal.SizeOf<NOTIFYICONDATAW>();

    private readonly string _className = $"YogaKeyboardMapperTray{Guid.NewGuid():N}";
    private readonly WndProcDelegate _wndProc;
    private readonly string _tooltip;
    private readonly string _showText;
    private readonly string _exitText;
    private readonly string? _iconPath;
    private IntPtr _hwnd;
    private IntPtr _icon;
    private bool _added;

    public TrayIcon(string tooltip, string showText, string exitText, string? iconPath)
    {
        _tooltip = tooltip;
        _showText = showText;
        _exitText = exitText;
        _iconPath = iconPath;
        _wndProc = WndProc;

        IntPtr instance = GetModuleHandleW(null);
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            lpszClassName = _className,
        };

        if (RegisterClassExW(ref wc) == 0 && Marshal.GetLastWin32Error() != ERROR_CLASS_ALREADY_EXISTS)
        {
            throw new InvalidOperationException("Tray window class registration failed.");
        }

        _hwnd = CreateWindowExW(0, _className, _className, 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, instance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("Tray window creation failed.");
        }

        _icon = LoadIcon();
        AddIcon();
    }

    /// <summary>The user clicked the icon or chose "Open" in its menu.</summary>
    public event Action? ShowRequested;

    /// <summary>The user chose "Exit" in the icon's menu.</summary>
    public event Action? ExitRequested;

    public void ShowBalloon(string title, string text)
    {
        if (!_added)
        {
            return;
        }

        var data = CreateData();
        data.uFlags = NIF_INFO;
        data.szInfoTitle = Truncate(title, 63);
        data.szInfo = Truncate(text, 255);
        data.dwInfoFlags = NIIF_INFO;
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero)
        {
            if (_added)
            {
                var data = CreateData();
                Shell_NotifyIconW(NIM_DELETE, ref data);
                _added = false;
            }

            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }

    private void AddIcon()
    {
        var data = CreateData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        _added = Shell_NotifyIconW(NIM_ADD, ref data);
        if (!_added)
        {
            Log.Write("tray icon: Shell_NotifyIcon failed");
        }
    }

    private NOTIFYICONDATAW CreateData() => new()
    {
        cbSize = (uint)DataSize,
        hWnd = _hwnd,
        uID = 1,
        uCallbackMessage = TrayCallback,
        hIcon = _icon,
        szTip = Truncate(_tooltip, 127),
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private IntPtr LoadIcon()
    {
        if (!string.IsNullOrEmpty(_iconPath) && File.Exists(_iconPath))
        {
            IntPtr icon = LoadImageW(IntPtr.Zero, _iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
            if (icon != IntPtr.Zero)
            {
                return icon;
            }
        }

        // Fall back to the executable's own icon.
        if (Environment.ProcessPath is { Length: > 0 } exe &&
            ExtractIconExW(exe, 0, out IntPtr large, out IntPtr small, 1) > 0)
        {
            if (small != IntPtr.Zero)
            {
                if (large != IntPtr.Zero)
                {
                    DestroyIcon(large);
                }

                return small;
            }

            return large;
        }

        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        IntPtr menu = CreatePopupMenu();
        AppendMenuW(menu, MF_STRING, IDM_SHOW, _showText);
        AppendMenuW(menu, MF_STRING, IDM_EXIT, _exitText);

        GetCursorPos(out POINT point);
        // Without this the menu stays on screen when the user clicks elsewhere.
        SetForegroundWindow(_hwnd);
        int command = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, point.X, point.Y, _hwnd, IntPtr.Zero);
        PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);

        if (command == IDM_SHOW)
        {
            ShowRequested?.Invoke();
        }
        else if (command == IDM_EXIT)
        {
            ExitRequested?.Invoke();
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == TaskbarCreatedMessage)
        {
            // Explorer restarted (e.g. after a crash), so the icon was lost.
            AddIcon();
            return IntPtr.Zero;
        }

        if (msg == TrayCallback)
        {
            switch ((int)lParam)
            {
                case 0x0202: // WM_LBUTTONUP
                case 0x0203: // WM_LBUTTONDBLCLK
                    ShowRequested?.Invoke();
                    return IntPtr.Zero;
                case WM_RBUTTONUP:
                case WM_CONTEXTMENU:
                    ShowMenu();
                    return IntPtr.Zero;
            }
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
