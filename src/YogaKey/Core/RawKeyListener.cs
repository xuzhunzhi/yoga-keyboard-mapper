using System.Runtime.InteropServices;

namespace YogaKey.Core;

/// <summary>
/// Listens for the vendor-defined HID reports that the Lenovo Yoga Book 9
/// Bluetooth keyboard emits for its "special" keys (Copilot / Lenovo / star ...).
///
/// Those keys never produce a normal Windows key event, so they cannot be
/// remapped with PowerToys, scancode maps or the registry. We therefore read
/// the raw HID input report and hand the vendor key code to the caller.
/// </summary>
public sealed class RawKeyListener : IDisposable
{
    private const int WM_INPUT = 0x00FF;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RIDEV_REMOVE = 0x00000001;
    private const uint RIDI_DEVICENAME = 0x20000007;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

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
        public IntPtr lpszMenuName;
        public IntPtr lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfoW(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    private readonly Dictionary<IntPtr, string> _deviceNames = new();
    private WndProcDelegate? _wndProc;
    private IntPtr _hwnd;
    private string _deviceMatch = string.Empty;
    private string _collectionMatch = string.Empty;

    public bool IsRunning { get; private set; }

    /// <summary>Vendor key code of a pressed special key (0x01 .. 0xFF).</summary>
    public event Action<byte>? SpecialKeyPressed;

    public bool Start(string deviceMatch, string collectionMatch)
    {
        _deviceMatch = deviceMatch ?? string.Empty;
        _collectionMatch = collectionMatch ?? string.Empty;

        if (_hwnd == IntPtr.Zero)
        {
            IntPtr hInstance = GetModuleHandleW(null);
            _wndProc = WndProc;

            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = hInstance,
                lpszClassName = Marshal.StringToHGlobalUni("YogaKeyRawInputSink"),
            };

            ushort atom = RegisterClassExW(ref wc);
            if (atom == 0)
            {
                return false;
            }

            _hwnd = CreateWindowExW(0, "YogaKeyRawInputSink", "YogaKeyRawInputSink", 0,
                0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                return false;
            }
        }

        if (!RegisterDevices(RIDEV_INPUTSINK))
        {
            IsRunning = false;
            return false;
        }

        IsRunning = true;
        return true;
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        RegisterDevices(RIDEV_REMOVE);
        IsRunning = false;
    }

    private bool RegisterDevices(uint flags)
    {
        bool allOk = true;
        // Yoga Book 9 keyboard collections: keyboard (1/6), system control (1/80),
        // consumer control (0C/1). The vendor reports arrive through one of them.
        (ushort page, ushort usage)[] usages =
        {
            (0x01, 0x06),
            (0x01, 0x80),
            (0x0C, 0x01),
        };

        foreach (var (page, usage) in usages)
        {
            var device = new RAWINPUTDEVICE
            {
                usUsagePage = page,
                usUsage = usage,
                dwFlags = flags,
                hwndTarget = _hwnd,
            };
            if (!RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            {
                allOk = false;
            }
        }

        return allOk;
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_INPUT)
        {
            try
            {
                HandleInput(lParam);
            }
            catch
            {
                // never let a malformed report kill the message loop
            }
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void HandleInput(IntPtr lParam)
    {
        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0)
        {
            return;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buffer, ref size, headerSize) == 0)
            {
                return;
            }

            byte[] data = new byte[size];
            Marshal.Copy(buffer, data, 0, (int)size);

            uint type = BitConverter.ToUInt32(data, 0);
            if (type != 2) // RIM_TYPEHID
            {
                return;
            }

            IntPtr device = (IntPtr)BitConverter.ToInt64(data, 8);
            int offset = (int)headerSize;
            if (offset + 8 > data.Length)
            {
                return;
            }

            uint reportSize = BitConverter.ToUInt32(data, offset);
            uint reportCount = BitConverter.ToUInt32(data, offset + 4);
            int payload = offset + 8;
            if (payload + reportSize > data.Length)
            {
                return;
            }

            string name = GetDeviceName(device);
            if (!Matches(name, _deviceMatch) || !Matches(name, _collectionMatch))
            {
                return;
            }

            if (reportSize != 4)
            {
                return;
            }

            // Report layout used by the Yoga Book 9 keyboard:
            //   [0] = 0x03 (report id)
            //   [1] = 0x10 on key down, 0x00 on key up
            //   [2] = 0x00
            //   [3] = vendor key code
            byte reportId = data[payload];
            byte state = data[payload + 1];
            byte code = data[payload + 3];

            if (reportId == 0x03 && state == 0x10 && code != 0)
            {
                SpecialKeyPressed?.Invoke(code);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool Matches(string value, string filter)
    {
        return string.IsNullOrEmpty(filter) ||
               value.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private string GetDeviceName(IntPtr device)
    {
        if (_deviceNames.TryGetValue(device, out string? cached))
        {
            return cached;
        }

        uint size = 0;
        GetRawInputDeviceInfoW(device, RIDI_DEVICENAME, IntPtr.Zero, ref size);
        if (size == 0)
        {
            return string.Empty;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)size * 2);
        try
        {
            if (GetRawInputDeviceInfoW(device, RIDI_DEVICENAME, buffer, ref size) == 0)
            {
                return string.Empty;
            }

            string name = Marshal.PtrToStringUni(buffer) ?? string.Empty;
            _deviceNames[device] = name;
            return name;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        Stop();
        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }
}
