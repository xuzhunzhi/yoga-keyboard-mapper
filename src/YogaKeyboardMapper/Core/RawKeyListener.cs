using System.Runtime.InteropServices;

namespace YogaKeyboardMapper.Core;

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
    private const uint RIM_TYPEHID = 2;
    private const int ERROR_CLASS_ALREADY_EXISTS = 1410;
    private const string WindowClassName = "YogaKeyboardMapperRawInputSink";
    private static readonly IntPtr HWND_MESSAGE = new(-3);
    private static readonly uint HeaderSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();

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

    private readonly Dictionary<IntPtr, bool> _deviceMatches = new();
    private WndProcDelegate? _wndProc;
    private IntPtr _hwnd;
    private IntPtr _buffer;
    private int _bufferSize;
    private string _deviceMatch = string.Empty;
    private string _collectionMatch = string.Empty;

    public bool IsRunning { get; private set; }

    /// <summary>Vendor key code of a pressed special key (0x01 .. 0xFF).</summary>
    public event Action<byte>? SpecialKeyPressed;

    public bool Start(string deviceMatch, string collectionMatch)
    {
        _deviceMatch = deviceMatch ?? string.Empty;
        _collectionMatch = collectionMatch ?? string.Empty;
        _deviceMatches.Clear();

        if (_hwnd == IntPtr.Zero)
        {
            IntPtr hInstance = GetModuleHandleW(null);
            _wndProc = WndProc;

            IntPtr className = Marshal.StringToHGlobalUni(WindowClassName);
            try
            {
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    style = 0,
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                    hInstance = hInstance,
                    lpszClassName = className,
                };

                if (RegisterClassExW(ref wc) == 0 && Marshal.GetLastWin32Error() != ERROR_CLASS_ALREADY_EXISTS)
                {
                    return false;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(className);
            }

            _hwnd = CreateWindowExW(0, WindowClassName, WindowClassName, 0,
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

        // Clear the flag first: even if unregistering fails, WndProc drops the input.
        IsRunning = false;
        RegisterDevices(RIDEV_REMOVE);
    }

    private bool RegisterDevices(uint flags)
    {
        bool allOk = true;
        // The vendor reports arrive as HID input (RIM_TYPEHID) on the consumer control
        // collection (0C/1, "Col05" on the Yoga Book 9); system control (1/80) is kept
        // for keyboards that route them there. The regular keyboard collection (1/6) is
        // NOT registered: it only ever yields RIM_TYPEKEYBOARD, which is ignored anyway,
        // and with INPUTSINK it would wake the UI thread for every key typed system-wide.
        (ushort page, ushort usage)[] usages =
        {
            (0x0C, 0x01),
            (0x01, 0x80),
        };

        bool remove = (flags & RIDEV_REMOVE) != 0;
        foreach (var (page, usage) in usages)
        {
            var device = new RAWINPUTDEVICE
            {
                usUsagePage = page,
                usUsage = usage,
                dwFlags = flags,
                // RIDEV_REMOVE fails unless hwndTarget is NULL.
                hwndTarget = remove ? IntPtr.Zero : _hwnd,
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
        if (msg == WM_INPUT && IsRunning)
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
        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, HeaderSize);
        if (size == 0)
        {
            return;
        }

        // One buffer reused for every message instead of an alloc + copy per report.
        if (size > _bufferSize)
        {
            if (_buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_buffer);
            }

            _buffer = Marshal.AllocHGlobal((int)size);
            _bufferSize = (int)size;
        }

        uint read = GetRawInputData(lParam, RID_INPUT, _buffer, ref size, HeaderSize);
        if (read == 0 || read == uint.MaxValue)
        {
            return;
        }

        if ((uint)Marshal.ReadInt32(_buffer, 0) != RIM_TYPEHID)
        {
            return;
        }

        int offset = (int)HeaderSize;
        if (offset + 8 > read)
        {
            return;
        }

        int reportSize = Marshal.ReadInt32(_buffer, offset);
        int reportCount = Marshal.ReadInt32(_buffer, offset + 4);
        if (reportSize != 4 || reportCount < 1)
        {
            return;
        }

        IntPtr device = Marshal.ReadIntPtr(_buffer, 8); // RAWINPUTHEADER.hDevice
        if (!DeviceMatches(device))
        {
            return;
        }

        // Report layout used by the Yoga Book 9 keyboard:
        //   [0] = 0x03 (report id)
        //   [1] = 0x10 on key down, 0x00 on key up
        //   [2] = 0x00
        //   [3] = vendor key code
        // Several reports can be batched into one WM_INPUT.
        for (int i = 0; i < reportCount; i++)
        {
            int report = offset + 8 + i * reportSize;
            if (report + reportSize > read)
            {
                break;
            }

            byte reportId = Marshal.ReadByte(_buffer, report);
            byte state = Marshal.ReadByte(_buffer, report + 1);
            byte code = Marshal.ReadByte(_buffer, report + 3);

            if (reportId == 0x03 && state == 0x10 && code != 0)
            {
                SpecialKeyPressed?.Invoke(code);
            }
        }
    }

    private static bool Matches(string value, string filter)
    {
        return string.IsNullOrEmpty(filter) ||
               value.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private bool DeviceMatches(IntPtr device)
    {
        if (_deviceMatches.TryGetValue(device, out bool cached))
        {
            return cached;
        }

        string? name = GetDeviceName(device);
        bool ok = Matches(name ?? string.Empty, _deviceMatch) && Matches(name ?? string.Empty, _collectionMatch);

        // A failed lookup is not cached, so a transient error does not disable the device.
        if (name is not null)
        {
            _deviceMatches[device] = ok;
        }

        return ok;
    }

    private static string? GetDeviceName(IntPtr device)
    {
        uint size = 0;
        GetRawInputDeviceInfoW(device, RIDI_DEVICENAME, IntPtr.Zero, ref size);
        if (size == 0)
        {
            return null;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)size * 2);
        try
        {
            uint read = GetRawInputDeviceInfoW(device, RIDI_DEVICENAME, buffer, ref size);
            if (read == 0 || read == uint.MaxValue)
            {
                return null;
            }

            return Marshal.PtrToStringUni(buffer);
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

        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
            _bufferSize = 0;
        }
    }
}
