using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using YogaKey.Core;

namespace YogaKey;

public sealed partial class EditKeyDialog : ContentDialog
{
    private readonly DispatcherTimer _captureTimer = new() { Interval = TimeSpan.FromSeconds(8) };

    public EditKeyDialog(int? code, string? action)
    {
        InitializeComponent();
        _captureTimer.Tick += OnCaptureTimeout;

        PresetCombo.ItemsSource = ActionPresets.All;
        PresetCombo.DisplayMemberPath = nameof(ActionPreset.Display);

        if (code is int value)
        {
            SetCode(value);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            ActionBox.Text = action;
        }

        PrimaryButtonClick += OnPrimaryButtonClick;
        IsPrimaryButtonEnabled = Code.HasValue;
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
        CaptureButton.Content = "重新捕获";
        IsCapturing = false;
        IsPrimaryButtonEnabled = true;
        ErrorBar.IsOpen = false;
    }

    private void OnCaptureClick(object sender, RoutedEventArgs e)
    {
        // A new capture invalidates the previous code, otherwise "Save" could
        // silently write the old key back while the UI says "waiting for a key".
        Code = null;
        IsPrimaryButtonEnabled = false;
        IsCapturing = true;
        CaptureButton.Content = "请按键…";
        CodeText.Text = "等待按键（8 秒）";
        ErrorBar.IsOpen = false;
        _captureTimer.Stop();
        _captureTimer.Start();
        CaptureRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCaptureTimeout(object? sender, object e)
    {
        _captureTimer.Stop();
        if (!IsCapturing)
        {
            return;
        }

        IsCapturing = false;
        CaptureButton.Content = "重新捕获";
        CodeText.Text = "尚未捕获";
        ShowError("没有收到按键。请确认 Yoga Book 键盘已连接后重试。");
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetCombo.SelectedItem is ActionPreset preset)
        {
            ActionBox.Text = preset.Action;
        }
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (Code is null)
        {
            args.Cancel = true;
            ShowError("请先捕获一个按键。");
            return;
        }

        if (Action.Length == 0)
        {
            args.Cancel = true;
            ShowError("请选择或填写一个动作。");
        }
    }

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
