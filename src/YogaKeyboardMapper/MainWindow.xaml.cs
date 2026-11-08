using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;

using WinRT.Interop;

using YogaKeyboardMapper.Core;

namespace YogaKeyboardMapper;

public sealed partial class MainWindow : Window
{
    private readonly RawKeyListener _listener = new();
    private readonly KeyboardWakeBooster _wakeBooster = new();
    private readonly ObservableCollection<MappingItem> _items = new();

    private AppConfig _config = new();
    private EditKeyDialog? _captureDialog;
    private bool _dialogOpen;
    private bool _initializing;
    private bool _ready;
    private int _statusToken;
    private double _titleBarIconScale;
    private IntPtr _windowHandle;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    public MainWindow()
    {
        InitializeComponent();

        Title = "Yoga Keyboard Mapper";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        Activated += OnWindowActivated;

        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "YogaKeyboardMapper.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
            RootGrid.Loaded += (_, _) =>
            {
                _ = LoadTitleBarIconAsync(iconPath);
                RootGrid.XamlRoot.Changed += (_, _) => _ = LoadTitleBarIconAsync(iconPath);
            };
        }

        Version version = typeof(MainWindow).Assembly.GetName().Version ?? new Version(1, 0, 0);
        VersionText.Text = Res.Format("VersionFormat", version.ToString(3));

        // If a previous run died between a key-down and its key-up, the user is
        // typing with a stuck modifier right now - clear it immediately.
        KeySender.ReleaseModifiers();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 640;
            presenter.PreferredMinimumHeight = 480;
        }

        _windowHandle = WindowNative.GetWindowHandle(this);
        ResizeAndCenter(920, 860);

        _config = AppConfig.Load(out string? configError);
        AutoStart.Refresh();
        MappingsList.ItemsSource = _items;
        _listener.SpecialKeyPressed += OnSpecialKey;

        _initializing = true;
        ListenToggle.IsOn = _config.ListenOnStartup;
        AutoStartToggle.IsOn = AutoStart.IsEnabled();
        FastWakeToggle.IsOn = _config.FastWake;
        DeviceMatchBox.Text = _config.DeviceMatch;
        CollectionBox.Text = _config.CollectionMatch;
        _initializing = false;

        _ready = true;
        if (ListenToggle.IsOn)
        {
            StartListening();
        }

        if (_config.FastWake)
        {
            _ = _wakeBooster.StartAsync(_config.DeviceMatch);
        }

        RefreshList();

        Closed += (_, _) =>
        {
            Log.Write("window closed");
            KeySender.ReleaseModifiers();
            _listener.Dispose();
            _wakeBooster.Dispose();
        };

        Log.Write($"app started, device={_config.DeviceMatch}, collection={_config.CollectionMatch}");

        if (configError is not null)
        {
            Log.Write("config load error: " + configError);
            ShowStatus(Res.Format("Status_ConfigLoadFailedFormat", configError), InfoBarSeverity.Error);
        }
    }

    public void Minimize()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Minimize();
        }
    }

    /// <summary>Restores and focuses the window (used when the app is launched a second time).</summary>
    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        SetForegroundWindow(_windowHandle);
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        bool inactive = args.WindowActivationState == WindowActivationState.Deactivated;
        string key = inactive ? "TextFillColorDisabledBrush" : "TextFillColorPrimaryBrush";

        TitleBarText.Foreground = (Brush)Application.Current.Resources[key];
        TitleBarIcon.Opacity = inactive ? 0.5 : 1.0;
    }

    /// <summary>
    /// Shows the .ico frame that matches the display scale. A plain BitmapImage does not
    /// pick the frame by size, so the icon came out blurry above 100 %.
    /// </summary>
    private async Task LoadTitleBarIconAsync(string path)
    {
        double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
        if (scale == _titleBarIconScale)
        {
            return;
        }

        _titleBarIconScale = scale;
        try
        {
            int target = (int)Math.Ceiling(TitleBarIcon.Width * scale);
            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            using IRandomAccessStream stream = await file.OpenReadAsync();
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);

            var frames = new List<BitmapFrame>();
            for (uint i = 0; i < decoder.FrameCount; i++)
            {
                frames.Add(await decoder.GetFrameAsync(i));
            }

            BitmapFrame frame = frames.Where(f => f.PixelWidth >= target).MinBy(f => f.PixelWidth)
                ?? frames.MaxBy(f => f.PixelWidth)!;
            SoftwareBitmap bitmap = await frame.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(bitmap);
            TitleBarIcon.Source = source;
        }
        catch (Exception ex)
        {
            Log.Write("title bar icon error: " + ex.Message);
        }
    }

    private void OnRootGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Explicit width beats MaxWidth + Stretch: the layout engine stays correct at
        // every window size, DPI and monitor arrangement, and the column stays centred.
        double width = e.NewSize.Width;
        if (width <= 0)
        {
            return;
        }

        double column = Math.Max(Math.Min(width, 1000.0), 320.0);
        PagePanel.Width = column;
        StatusBar.Width = Math.Max(column - 64.0, 256.0);
    }

    /// <summary>
    /// Sizes the window in DIPs, clamped to the work area, then centres it.
    /// AppWindow.Resize takes physical pixels, so the DPI scale has to be applied
    /// here or the window overflows the screen after a resolution/DPI change.
    /// </summary>
    private void ResizeAndCenter(int widthDips, int heightDips)
    {
        IntPtr hwnd = WindowNative.GetWindowHandle(this);
        uint dpi = GetDpiForWindow(hwnd);
        double scale = dpi == 0 ? 1.0 : dpi / 96.0;

        DisplayArea display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        int maxWidth = Math.Max(display.WorkArea.Width - 48, 480);
        int maxHeight = Math.Max(display.WorkArea.Height - 48, 360);

        int width = Math.Min((int)(widthDips * scale), maxWidth);
        int height = Math.Min((int)(heightDips * scale), maxHeight);

        AppWindow.Resize(new SizeInt32(width, height));

        int x = display.WorkArea.X + (display.WorkArea.Width - width) / 2;
        int y = display.WorkArea.Y + (display.WorkArea.Height - height) / 2;
        AppWindow.Move(new PointInt32(Math.Max(x, 0), Math.Max(y, 0)));
    }

    // ---------------------------------------------------------------- listener

    private bool StartListening()
    {
        bool ok = _listener.IsRunning || _listener.Start(_config.DeviceMatch, _config.CollectionMatch);

        _initializing = true;
        ListenToggle.IsOn = ok;
        _initializing = false;

        Log.Write($"listener start => {ok}");
        if (!ok)
        {
            ShowStatus(Res.Get("Status_ListenFailed"), InfoBarSeverity.Error);
        }

        return ok;
    }

    private void OnListenToggled(object sender, RoutedEventArgs e)
    {
        if (!_ready || _initializing)
        {
            return;
        }

        if (ListenToggle.IsOn)
        {
            StartListening();
        }
        else
        {
            _listener.Stop();
            ShowStatus(Res.Get("Status_ListenPaused"), InfoBarSeverity.Informational);
        }

        _config.ListenOnStartup = ListenToggle.IsOn;
        TrySave();
    }

    private void OnSpecialKey(byte code)
    {
        if (_captureDialog is { IsCapturing: true } dialog)
        {
            dialog.SetCode(code);
            _captureDialog = null;
            return;
        }

        KeyMapping? mapping = _config.Mappings.FirstOrDefault(m => m.Code == code);
        if (mapping is null)
        {
            return;
        }

        bool ok = KeySender.Execute(mapping.Action);
        Log.Write($"0x{code:X2} => {mapping.Action} ok={ok}");
        if (!ok)
        {
            ShowStatus(Res.Format("Status_ActionFailedFormat", mapping.Action), InfoBarSeverity.Error);
        }
    }

    // ------------------------------------------------------------------ editor

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        await ShowEditorAsync(null, null);
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (MappingsList.SelectedItem is MappingItem item)
        {
            await ShowEditorAsync(item.Code, item.Action);
        }
    }

    private void OnRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // Hit test on the row itself: double-clicking the empty space under the
        // last row must not open whatever happens to be selected.
        if (sender is FrameworkElement { DataContext: MappingItem item })
        {
            e.Handled = true;
            _ = ShowEditorAsync(item.Code, item.Action);
        }
    }

    private async void OnRowEditClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MappingItem item })
        {
            await ShowEditorAsync(item.Code, item.Action);
        }
    }

    private async void OnRowDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MappingItem item })
        {
            await DeleteMappingAsync(item);
        }
    }

    private async Task ShowEditorAsync(int? code, string? action)
    {
        if (_dialogOpen)
        {
            return;
        }

        _dialogOpen = true;
        EditKeyDialog? dialog = null;
        try
        {
            dialog = new EditKeyDialog(code, action, _windowHandle)
            {
                XamlRoot = RootGrid.XamlRoot,
            };

            dialog.CaptureRequested += OnDialogCaptureRequested;
            ContentDialogResult result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && dialog.Code is int captured)
            {
                SaveMapping(captured, dialog.Action);
            }
        }
        catch (Exception ex)
        {
            Log.Write("editor error: " + ex.Message);
            ShowStatus(Res.Get("Status_EditorFailed"), InfoBarSeverity.Error);
        }
        finally
        {
            if (dialog is not null)
            {
                dialog.CaptureRequested -= OnDialogCaptureRequested;
            }

            if (ReferenceEquals(_captureDialog, dialog))
            {
                _captureDialog = null;
            }

            _dialogOpen = false;
        }
    }

    private void OnDialogCaptureRequested(object? sender, EventArgs e)
    {
        if (!_listener.IsRunning)
        {
            StartListening();
        }

        _captureDialog = sender as EditKeyDialog;
    }

    private void SaveMapping(int code, string action)
    {
        KeyMapping? existing = _config.Mappings.FirstOrDefault(m => m.Code == code);
        if (existing is null)
        {
            _config.Mappings.Add(new KeyMapping { Code = code, Action = action });
        }
        else
        {
            existing.Action = action;
        }

        if (!TrySave())
        {
            return;
        }

        RefreshList();

        MappingItem? saved = _items.FirstOrDefault(i => i.Code == code);
        if (saved is not null)
        {
            MappingsList.SelectedItem = saved;
            MappingsList.ScrollIntoView(saved);
        }

        ShowStatus(Res.Format("Status_SavedFormat", MappingItem.FormatCode(code), action), InfoBarSeverity.Success);
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (MappingsList.SelectedItem is MappingItem item)
        {
            await DeleteMappingAsync(item);
        }
    }

    private async Task DeleteMappingAsync(MappingItem item)
    {
        if (_dialogOpen)
        {
            return;
        }

        _dialogOpen = true;
        try
        {
            var confirm = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
                Title = Res.Get("DeleteDialog_Title"),
                Content = Res.Format("DeleteDialog_ContentFormat", item.KeyLabel, item.ActionText),
                PrimaryButtonText = Res.Get("DeleteDialog_Primary"),
                CloseButtonText = Res.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Close,
            };

            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Write("delete dialog error: " + ex.Message);
        }
        finally
        {
            _dialogOpen = false;
        }

        int removed = _config.Mappings.RemoveAll(m => m.Code == item.Code);
        if (removed == 0 || !TrySave())
        {
            return;
        }

        RefreshList();
        ShowStatus(Res.Format("Status_DeletedFormat", item.KeyLabel), InfoBarSeverity.Success);
    }

    private void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (MappingsList.SelectedItem is not MappingItem item)
        {
            return;
        }

        if (e.Key == VirtualKey.Delete)
        {
            OnDeleteClick(sender, e);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Enter)
        {
            _ = ShowEditorAsync(item.Code, item.Action);
            e.Handled = true;
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        bool hasSelection = MappingsList.SelectedItem is MappingItem;
        EditButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
    }

    private void RefreshList()
    {
        int previous = MappingsList.SelectedItem is MappingItem selected ? selected.Code : -1;

        _items.Clear();
        foreach (KeyMapping mapping in _config.Mappings.OrderBy(m => m.Code))
        {
            _items.Add(new MappingItem { Code = mapping.Code, Action = mapping.Action });
        }

        bool empty = _items.Count == 0;
        ListCard.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

        if (!empty)
        {
            MappingItem? restored = previous >= 0 ? _items.FirstOrDefault(i => i.Code == previous) : null;
            MappingsList.SelectedItem = restored ?? _items[0];
        }

        UpdateSelectionState();
    }

    // ---------------------------------------------------------------- settings

    private bool TrySave()
    {
        try
        {
            _config.Save();
            return true;
        }
        catch (Exception ex)
        {
            Log.Write("save error: " + ex.Message);
            ShowStatus(Res.Format("Status_SaveFailedFormat", ex.Message), InfoBarSeverity.Error);
            return false;
        }
    }

    private async void OnAutoStartToggled(object sender, RoutedEventArgs e)
    {
        if (!_ready || _initializing)
        {
            return;
        }

        bool requested = AutoStartToggle.IsOn;
        bool enabled;
        try
        {
            enabled = await AutoStart.SetAsync(requested);
        }
        catch (Exception ex)
        {
            Log.Write("autostart error: " + ex.Message);
            enabled = AutoStart.IsEnabled();
        }

        if (enabled != AutoStartToggle.IsOn)
        {
            _initializing = true;
            AutoStartToggle.IsOn = enabled;
            _initializing = false;
        }

        _config.StartWithWindows = enabled;
        TrySave();
        if (requested && !enabled)
        {
            ShowStatus(Res.Get("Status_AutoStartBlocked"), InfoBarSeverity.Warning);
            return;
        }

        ShowStatus(Res.Get(enabled ? "Status_AutoStartOn" : "Status_AutoStartOff"), InfoBarSeverity.Informational);
    }

    private void OnFastWakeToggled(object sender, RoutedEventArgs e)
    {
        if (!_ready || _initializing)
        {
            return;
        }

        if (FastWakeToggle.IsOn)
        {
            _ = _wakeBooster.StartAsync(_config.DeviceMatch);
        }
        else
        {
            _wakeBooster.Stop();
        }

        _config.FastWake = FastWakeToggle.IsOn;
        TrySave();
    }

    private void OnApplyDeviceClick(object sender, RoutedEventArgs e)
    {
        string device = DeviceMatchBox.Text.Trim();
        string collection = CollectionBox.Text.Trim();

        if (device.Length == 0 && collection.Length == 0)
        {
            ShowStatus(Res.Get("Status_DeviceMatchEmpty"), InfoBarSeverity.Warning);
            return;
        }

        _config.DeviceMatch = device;
        _config.CollectionMatch = collection;

        // Show the normalised value instead of whatever the user typed.
        DeviceMatchBox.Text = device;
        CollectionBox.Text = collection;

        if (!TrySave())
        {
            return;
        }

        _listener.Stop();
        bool ok = StartListening();
        if (_config.FastWake)
        {
            _ = _wakeBooster.StartAsync(_config.DeviceMatch);
        }

        // StartListening already raised the error InfoBar; do not overwrite it with "applied".
        ShowStatus(
            Res.Get(ok ? "Status_DeviceApplied" : "Status_DeviceSavedNotListening"),
            ok ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppConfig.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", AppConfig.Folder) { UseShellExecute = true });
    }

    private async void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        AutomationProperties.SetLiveSetting(
            StatusBar,
            severity == InfoBarSeverity.Error ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
        StatusBar.IsOpen = true;

        int token = ++_statusToken;
        if (severity == InfoBarSeverity.Error)
        {
            return; // errors stay until the user closes them
        }

        try
        {
            await Task.Delay(5000);
        }
        catch
        {
            return;
        }

        if (token == _statusToken)
        {
            StatusBar.IsOpen = false;
        }
    }
}
