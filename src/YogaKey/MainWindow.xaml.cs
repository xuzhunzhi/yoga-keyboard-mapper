using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using Windows.Graphics;
using Windows.System;

using WinRT.Interop;

using YogaKey.Core;

namespace YogaKey;

public sealed partial class MainWindow : Window
{
    private readonly RawKeyListener _listener = new();
    private readonly ObservableCollection<MappingItem> _items = new();

    private AppConfig _config = new();
    private EditKeyDialog? _captureDialog;
    private bool _dialogOpen;
    private bool _initializing;
    private bool _ready;
    private int _statusToken;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    public MainWindow()
    {
        InitializeComponent();

        Title = "YogaKey";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // If a previous run died between a key-down and its key-up, the user is
        // typing with a stuck modifier right now - clear it immediately.
        KeySender.ReleaseModifiers();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 640;
            presenter.PreferredMinimumHeight = 480;
        }

        ResizeAndCenter(920, 820);

        _config = AppConfig.Load();
        MappingsList.ItemsSource = _items;
        _listener.SpecialKeyPressed += OnSpecialKey;

        _initializing = true;
        ListenToggle.IsOn = _config.ListenOnStartup;
        AutoStartToggle.IsOn = AutoStart.IsEnabled();
        DeviceMatchBox.Text = _config.DeviceMatch;
        CollectionBox.Text = _config.CollectionMatch;
        _initializing = false;

        _ready = true;
        if (ListenToggle.IsOn)
        {
            StartListening();
        }

        RefreshList();

        Closed += (_, _) =>
        {
            Log.Write("window closed");
            KeySender.ReleaseModifiers();
            _listener.Dispose();
        };

        Log.Write($"app started, device={_config.DeviceMatch}, collection={_config.CollectionMatch}");
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
            ShowStatus("监听启动失败：检查 Yoga Book 键盘是否已连接，或到「高级」核对设备匹配。", InfoBarSeverity.Error);
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
            ShowStatus("已暂停监听。", InfoBarSeverity.Informational);
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
            ShowStatus($"动作执行失败：{mapping.Action}", InfoBarSeverity.Error);
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
            dialog = new EditKeyDialog(code, action)
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
            ShowStatus("编辑窗口打开失败，请重试。", InfoBarSeverity.Error);
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

        ShowStatus($"已保存 {MappingItem.FormatCode(code)} → {action}", InfoBarSeverity.Success);
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
                Title = "删除映射？",
                Content = $"{item.KeyLabel} → {item.ActionText}",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
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
        ShowStatus($"已删除 {item.KeyLabel}", InfoBarSeverity.Success);
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
            ShowStatus($"保存配置失败：{ex.Message}", InfoBarSeverity.Error);
            return false;
        }
    }

    private void OnAutoStartToggled(object sender, RoutedEventArgs e)
    {
        if (!_ready || _initializing)
        {
            return;
        }

        AutoStart.Set(AutoStartToggle.IsOn);
        _config.StartWithWindows = AutoStartToggle.IsOn;
        TrySave();
        ShowStatus(AutoStartToggle.IsOn ? "已设置开机自启。" : "已取消开机自启。", InfoBarSeverity.Informational);
    }

    private void OnApplyDeviceClick(object sender, RoutedEventArgs e)
    {
        string device = DeviceMatchBox.Text.Trim();
        string collection = CollectionBox.Text.Trim();

        if (device.Length == 0 && collection.Length == 0)
        {
            ShowStatus("设备匹配不能全部留空，否则会拦截所有键盘。", InfoBarSeverity.Warning);
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

        // StartListening already raised the error InfoBar; do not overwrite it with "applied".
        ShowStatus(
            ok ? "设备匹配已应用。" : "设备匹配已保存，但监听没有启动成功。",
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
