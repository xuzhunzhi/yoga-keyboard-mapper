using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.Storage;
using Windows.Storage.Pickers;

using WinRT.Interop;

using YogaKeyboardMapper.Core;

namespace YogaKeyboardMapper;

public sealed partial class EditKeyDialog : ContentDialog
{
    private readonly DispatcherTimer _captureTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly IntPtr _windowHandle;
    private bool _syncingAction;
    private bool _syncingPreset;

    public EditKeyDialog(int? code, string? action, IntPtr windowHandle)
    {
        InitializeComponent();
        _captureTimer.Tick += OnCaptureTimeout;
        _windowHandle = windowHandle;

        Title = Res.Get(code is null ? "EditDialog_AddTitle" : "EditDialog_EditTitle");
        CodeText.Text = Res.Get("Capture_NotCaptured");
        CaptureButton.Content = Res.Get("Capture_Start");

        CategoryBox.ItemsSource = ActionPresets.Categories;

        if (code is int value)
        {
            SetCode(value);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            ActionBox.Text = action;
        }

        ShowPreset(FindPreset(ActionBox.Text));

        PrimaryButtonClick += OnPrimaryButtonClick;
        ActionBox.TextChanged += OnActionTextChanged;
    }

    public int? Code { get; private set; }

    public bool IsCapturing { get; private set; }

    public string Action => ActionBox.Text.Trim();

    /// <summary>Raised when the user asks the window to start listening for a key press.</summary>
    public event EventHandler? CaptureRequested;

    public void SetCode(int code)
    {
        _captureTimer.Stop();
        Code = code;
        CodeText.Text = $"0x{code:X2}";
        CaptureButton.Content = Res.Get("Capture_Retry");
        IsCapturing = false;
        ErrorBar.IsOpen = false;
    }

    private async void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
            };
            picker.FileTypeFilter.Add(".exe");
            picker.FileTypeFilter.Add(".lnk");
            picker.FileTypeFilter.Add(".bat");
            picker.FileTypeFilter.Add(".cmd");

            // WinUI 3 desktop pickers need an owner window or they throw.
            InitializeWithWindow.Initialize(picker, _windowHandle);

            StorageFile file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            ActionBox.Text = $"run:\"{file.Path}\"";
            ErrorBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            ShowError(Res.Format("Error_BrowseFailedFormat", ex.Message));
        }
    }

    private void OnCaptureClick(object sender, RoutedEventArgs e)
    {
        if (IsCapturing)
        {
            CancelCapture();
            return;
        }

        // A new capture invalidates the previous code, otherwise "Save" could
        // silently write the old key back while the UI says "waiting for a key".
        Code = null;
        IsCapturing = true;
        CaptureButton.Content = Res.Get("Capture_Cancel");
        CodeText.Text = Res.Get("Capture_Waiting");
        ErrorBar.IsOpen = false;
        _captureTimer.Stop();
        _captureTimer.Start();
        CaptureRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CancelCapture()
    {
        _captureTimer.Stop();
        IsCapturing = false;
        CaptureButton.Content = Res.Get(Code.HasValue ? "Capture_Retry" : "Capture_Start");
        CodeText.Text = Code.HasValue ? $"0x{Code:X2}" : Res.Get("Capture_NotCaptured");
    }

    private void OnCaptureTimeout(object? sender, object e)
    {
        _captureTimer.Stop();
        if (!IsCapturing)
        {
            return;
        }

        CancelCapture();
        ShowError(Res.Get("Error_NoKeyReceived"));
    }

    private static ActionPreset? FindPreset(string action)
    {
        string text = action.Trim();
        return ActionPresets.All.FirstOrDefault(p => string.Equals(p.Action, text, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Selects <paramref name="preset"/> in both combo boxes, or clears the preset box for a custom action.</summary>
    private void ShowPreset(ActionPreset? preset)
    {
        _syncingPreset = true;
        try
        {
            PresetCategory? category = preset is null
                ? CategoryBox.SelectedItem as PresetCategory ?? ActionPresets.Categories[0]
                : ActionPresets.Categories.First(c => c.Key == preset.CategoryKey);

            if (!ReferenceEquals(CategoryBox.SelectedItem, category))
            {
                CategoryBox.SelectedItem = category;
                PresetBox.ItemsSource = category.Presets.ToList();
            }

            PresetBox.SelectedItem = preset;
        }
        finally
        {
            _syncingPreset = false;
        }
    }

    private void OnActionTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingAction)
        {
            return;
        }

        // Keep the preset boxes in sync when the user edits the action by hand.
        ShowPreset(FindPreset(ActionBox.Text));
    }

    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingPreset || CategoryBox.SelectedItem is not PresetCategory category)
        {
            return;
        }

        _syncingPreset = true;
        try
        {
            PresetBox.ItemsSource = category.Presets.ToList();
            ActionPreset? current = FindPreset(ActionBox.Text);
            PresetBox.SelectedItem = current?.CategoryKey == category.Key ? current : null;
        }
        finally
        {
            _syncingPreset = false;
        }
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingPreset || PresetBox.SelectedItem is not ActionPreset preset)
        {
            return;
        }

        _syncingAction = true;
        try
        {
            ActionBox.Text = preset.Action;
        }
        finally
        {
            _syncingAction = false;
        }

        ErrorBar.IsOpen = false;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // The primary button stays enabled on purpose: pressing it is the only way
        // the user gets told *why* the dialog cannot be saved yet.
        if (Code is null)
        {
            args.Cancel = true;
            ShowError(Res.Get("Error_CaptureFirst"));
            CaptureButton.Focus(FocusState.Programmatic);
            return;
        }

        if (Action.Length == 0)
        {
            args.Cancel = true;
            ShowError(Res.Get("Error_NoAction"));
            ActionBox.Focus(FocusState.Programmatic);
        }
    }

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
