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
    private bool _initializing;
    private bool _ready;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    public MainWindow()
    {
        InitializeComponent();

        Title = "YogaKey";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 640;
            presenter.PreferredMinimumHeight = 480;
        }

        ResizeToDips(920, 820);

        _config = AppConfig.Load();
        MappingsList.ItemsSource = _items;
        _listener.SpecialKeyPressed += OnSpecialKey;

        _initializing = true;
        ListenToggle.IsOn = true;
        AutoStartToggle.IsOn = AutoStart.IsEnabled();
        DeviceMatchBox.Text = _config.DeviceMatch;
        CollectionBox.Text = _config.CollectionMatch;
        _initializing = false;

        _ready = true;
        StartListening();
        RefreshList();

        Closed += (_, _) =>
        {
            Log.Write("window closed");
            _listener.Dispose();
        };

        Log.Write($"app started, device={_config.DeviceMatch}, collection={_config.CollectionMatch}");
    }

    private void ResizeToDips(int width, int height)
    {
        IntPtr hwnd = WindowNative.GetWindowHandle(this);
        uint dpi = GetDpiForWindow(hwnd);
        double scale = dpi == 0 ? 1.0 : dpi / 96.0;
        AppWindow.Resize(new SizeInt32((int)(width * scale), (int)(height * scale)));
    }

    // ---------------------------------------------------------------- listener

    private void StartListening()
    {
        bool ok = _listener.IsRunning || _listener.Start(_config.DeviceMatch, _config.CollectionMatch);

        _initializing = true;
        ListenToggle.IsOn = ok;
        _initializing = false;

        Log.Write($"listener start => {ok}");
        if (!ok)
        {
            ShowStatus("监听启动失败：Raw Input 注册被拒绝。", InfoBarSeverity.Error);
        }
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

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MappingItem item)
        {
            _ = ShowEditorAsync(item.Code, item.Action);
        }
    }

    private async Task ShowEditorAsync(int? code, string? action)
    {
        var dialog = new EditKeyDialog(code, action)
        {
            XamlRoot = RootGrid.XamlRoot,
        };

        dialog.CaptureRequested += OnDialogCaptureRequested;
        ContentDialogResult result = await dialog.ShowAsync();
        dialog.CaptureRequested -= OnDialogCaptureRequested;

        if (ReferenceEquals(_captureDialog, dialog))
        {
            _captureDialog = null;
        }

        if (result == ContentDialogResult.Primary && dialog.Code is int captured)
        {
            SaveMapping(captured, dialog.Action);
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

        _config.Save();
        RefreshList();

        MappingItem? saved = _items.FirstOrDefault(i => i.Code == code);
        if (saved is not null)
        {
            MappingsList.SelectedItem = saved;
        }

        ShowStatus($"已保存 0x{code:X2} → {action}", InfoBarSeverity.Success);
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (MappingsList.SelectedItem is not MappingItem item)
        {
            return;
        }

        int removed = _config.Mappings.RemoveAll(m => m.Code == item.Code);
        if (removed == 0)
        {
            return;
        }

        _config.Save();
        RefreshList();
        ShowStatus($"已删除 0x{item.Code:X2}", InfoBarSeverity.Success);
    }

    private void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Delete)
        {
            OnDeleteClick(sender, e);
            e.Handled = true;
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
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

        EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (previous >= 0)
        {
            MappingsList.SelectedItem = _items.FirstOrDefault(i => i.Code == previous);
        }

        OnSelectionChanged(this, null!);
    }

    // ---------------------------------------------------------------- settings

    private void OnAutoStartToggled(object sender, RoutedEventArgs e)
    {
        if (!_ready || _initializing)
        {
            return;
        }

        AutoStart.Set(AutoStartToggle.IsOn);
        ShowStatus(AutoStartToggle.IsOn ? "已设置开机自启。" : "已取消开机自启。", InfoBarSeverity.Informational);
    }

    private void OnApplyDeviceClick(object sender, RoutedEventArgs e)
    {
        _config.DeviceMatch = DeviceMatchBox.Text.Trim();
        _config.CollectionMatch = CollectionBox.Text.Trim();
        _config.Save();

        _listener.Stop();
        StartListening();
        ShowStatus("设备匹配已应用。", InfoBarSeverity.Success);
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppConfig.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", AppConfig.Folder) { UseShellExecute = true });
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }
}
