using System.Runtime.InteropServices;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

using YogaKeyboardMapper.Core;

namespace YogaKeyboardMapper;

public partial class App : Application
{
    private const string MutexName = @"Local\YogaKeyboardMapper.SingleInstance";
    private const string ShowEventName = @"Local\YogaKeyboardMapper.Show";
    private const uint ASFW_ANY = 0xFFFFFFFF;

    private MainWindow? _window;
    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;

    public App()
    {
        InitializeComponent();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Hand focus to the running instance and let it bring its window up.
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out EventWaitHandle? show))
            {
                using (show)
                {
                    AllowSetForegroundWindow(ASFW_ANY);
                    show.Set();
                }
            }
            else
            {
                MessageBoxW(IntPtr.Zero, Res.Get("App_AlreadyRunning"), "Yoga Keyboard Mapper", 0x00000040);
            }

            Exit();
            return;
        }

        // The Run entry passes --minimized; the MSIX startup task can't pass arguments.
        bool minimized = Environment.GetCommandLineArgs()
            .Skip(1)
            .Any(a => string.Equals(a, AutoStart.MinimizedArg, StringComparison.OrdinalIgnoreCase)) ||
            (AppPackage.IsPackaged &&
             AppInstance.GetCurrent().GetActivatedEventArgs().Kind == ExtendedActivationKind.StartupTask);

        _window = new MainWindow();

        DispatcherQueue queue = DispatcherQueue.GetForCurrentThread();
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, _) => queue.TryEnqueue(() => _window?.BringToFront()),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        _window.Closed += (_, _) =>
        {
            _showWait?.Unregister(null);
            _showEvent?.Dispose();
        };

        _window.Activate();
        if (minimized)
        {
            _window.Minimize();
        }
    }
}
