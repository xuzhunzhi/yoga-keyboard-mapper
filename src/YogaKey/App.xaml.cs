using System.Runtime.InteropServices;

using Microsoft.UI.Xaml;

namespace YogaKey;

public partial class App : Application
{
    private Window? _window;
    private Mutex? _mutex;

    public App()
    {
        InitializeComponent();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mutex = new Mutex(true, @"Local\YogaKey.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBoxW(IntPtr.Zero, "YogaKey 已经在运行了（看任务栏）。", "YogaKey", 0x00000040);
            Exit();
            return;
        }

        _window = new MainWindow();
        _window.Activate();
    }
}
