using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using Microsoft.Windows.System.Power;

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace YogaKeyboardMapper.Core;

/// <summary>
/// Shortens the delay before a sleeping Bluetooth keyboard types again.
///
/// The Yoga Book keyboard drops its connection after about 5 minutes idle. Windows then
/// looks for it with the HID-over-GATT "reduced power" scan (11.25 ms every 1.28 s), so
/// the first key press can take seconds to reconnect. While the keyboard is disconnected
/// this runs a passive scan, which raises the radio's scan rate and lets Windows reconnect
/// in about 0.2 s. Only HID advertisements are delivered to the app, so the CPU cost stays
/// negligible. The scan stops as soon as the keyboard is connected, the screen turns off
/// or Energy Saver comes on.
/// </summary>
public sealed partial class KeyboardWakeBooster : IDisposable
{
    private const uint RIDI_DEVICENAME = 0x20000007;
    private const ushort HidServiceShortId = 0x1812;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICELIST
    {
        public IntPtr hDevice;
        public uint dwType;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList([Out] RAWINPUTDEVICELIST[]? pRawInputDeviceList, ref uint puiNumDevices, uint cbSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfoW(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

    // "...{00001812-0000-1000-8000-00805f9b34fb}_Dev_VID&010461_PID&4f2b_REV&0020_fe5a42b74943&Col05#..."
    [GeneratedRegex(@"\{00001812-0000-1000-8000-00805f9b34fb\}_.*?_([0-9a-f]{12})[&#]", RegexOptions.IgnoreCase)]
    private static partial Regex BleHidAddress();

    private readonly object _gate = new();
    private readonly List<BluetoothLEDevice> _devices = new();
    private BluetoothLEAdvertisementWatcher? _watcher;
    private bool _running;
    private int _generation;

    /// <summary>
    /// Starts watching the Bluetooth LE keyboards whose device path contains
    /// <paramref name="deviceMatch"/> (the same matching the key listener uses).
    /// </summary>
    public async Task StartAsync(string deviceMatch)
    {
        Stop();
        int generation;
        lock (_gate)
        {
            generation = ++_generation;
        }

        var found = new List<BluetoothLEDevice>();
        foreach (ulong address in FindKeyboardAddresses(deviceMatch))
        {
            try
            {
                if (await BluetoothLEDevice.FromBluetoothAddressAsync(address) is { } device)
                {
                    found.Add(device);
                }
            }
            catch (Exception ex)
            {
                Log.Write($"wake boost: {address:X12} unavailable: {ex.Message}");
            }
        }

        lock (_gate)
        {
            if (generation != _generation)
            {
                // Stopped or restarted while the lookup was running.
                found.ForEach(d => d.Dispose());
                return;
            }

            foreach (BluetoothLEDevice device in found)
            {
                device.ConnectionStatusChanged += OnConnectionStatusChanged;
                _devices.Add(device);
            }

            PowerManager.DisplayStatusChanged += OnPowerChanged;
            PowerManager.EnergySaverStatusChanged += OnPowerChanged;
            _running = true;
            Log.Write($"wake boost: watching {string.Join(", ", found.Select(d => $"{d.BluetoothAddress:X12} ({d.ConnectionStatus})"))}");
            UpdateScan();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _generation++;
            if (!_running)
            {
                return;
            }

            _running = false;
            PowerManager.DisplayStatusChanged -= OnPowerChanged;
            PowerManager.EnergySaverStatusChanged -= OnPowerChanged;
            foreach (BluetoothLEDevice device in _devices)
            {
                device.ConnectionStatusChanged -= OnConnectionStatusChanged;
                device.Dispose();
            }

            _devices.Clear();
            UpdateScan();
        }
    }

    public void Dispose() => Stop();

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        lock (_gate)
        {
            UpdateScan();
        }
    }

    private void OnPowerChanged(object? sender, object args)
    {
        lock (_gate)
        {
            UpdateScan();
        }
    }

    /// <summary>Starts or stops the scan to match the current state. Call under <see cref="_gate"/>.</summary>
    private void UpdateScan()
    {
        bool wanted = false;
        if (_running && _devices.Any(d => d.ConnectionStatus == BluetoothConnectionStatus.Disconnected))
        {
            try
            {
                wanted = PowerManager.DisplayStatus != DisplayStatus.Off &&
                         PowerManager.EnergySaverStatus != EnergySaverStatus.On;
            }
            catch
            {
                wanted = true;
            }
        }

        if (wanted == (_watcher is not null))
        {
            return;
        }

        if (!wanted)
        {
            _watcher!.Stopped -= OnWatcherStopped;
            _watcher.Stop();
            _watcher = null;
            Log.Write("wake boost: scan off");
            return;
        }

        try
        {
            var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Passive };
            // The radio scans every channel either way; the filter only keeps other
            // devices' advertisements from waking this process.
            watcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(BluetoothUuidHelper.FromShortId(HidServiceShortId));
            watcher.Received += (_, _) => { };
            watcher.Stopped += OnWatcherStopped;
            watcher.Start();
            _watcher = watcher;
            Log.Write("wake boost: scan on");
        }
        catch (Exception ex)
        {
            Log.Write("wake boost: scan failed: " + ex.Message);
        }
    }

    /// <summary>The scan ended on its own (e.g. Bluetooth was turned off).</summary>
    private void OnWatcherStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        lock (_gate)
        {
            if (ReferenceEquals(sender, _watcher))
            {
                _watcher = null;
                Log.Write("wake boost: scan stopped: " + args.Error);
            }
        }
    }

    /// <summary>Bluetooth addresses of the BLE HID devices whose raw-input path contains <paramref name="deviceMatch"/>.</summary>
    private static IEnumerable<ulong> FindKeyboardAddresses(string deviceMatch)
    {
        if (string.IsNullOrWhiteSpace(deviceMatch))
        {
            return Array.Empty<ulong>();
        }

        var addresses = new HashSet<ulong>();
        uint count = 0;
        uint size = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
        if (GetRawInputDeviceList(null, ref count, size) != 0 || count == 0)
        {
            return addresses;
        }

        var list = new RAWINPUTDEVICELIST[count];
        uint got = GetRawInputDeviceList(list, ref count, size);
        if (got == uint.MaxValue)
        {
            return addresses;
        }

        for (int i = 0; i < got; i++)
        {
            string name = GetDeviceName(list[i].hDevice);
            if (name.Contains(deviceMatch, StringComparison.OrdinalIgnoreCase) &&
                BleHidAddress().Match(name) is { Success: true } m)
            {
                addresses.Add(Convert.ToUInt64(m.Groups[1].Value, 16));
            }
        }

        return addresses;
    }

    private static string GetDeviceName(IntPtr device)
    {
        uint chars = 0;
        GetRawInputDeviceInfoW(device, RIDI_DEVICENAME, IntPtr.Zero, ref chars);
        if (chars == 0)
        {
            return string.Empty;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)chars * 2);
        try
        {
            return GetRawInputDeviceInfoW(device, RIDI_DEVICENAME, buffer, ref chars) == uint.MaxValue
                ? string.Empty
                : Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
