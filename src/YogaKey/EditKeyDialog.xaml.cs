using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using YogaKey.Core;

namespace YogaKey;

public sealed partial class EditKeyDialog : ContentDialog
{
    public EditKeyDialog(int? code, string? action)
    {
        InitializeComponent();

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
        Code = code;
        CodeText.Text = $"0x{code:X2}";
        CaptureButton.Content = "重新捕获";
        IsCapturing = false;
        IsPrimaryButtonEnabled = true;
        ErrorBar.IsOpen = false;
    }

    private void OnCaptureClick(object sender, RoutedEventArgs e)
    {
        IsCapturing = true;
        CaptureButton.Content = "请按键…";
        CodeText.Text = "等待按键";
        ErrorBar.IsOpen = false;
        CaptureRequested?.Invoke(this, EventArgs.Empty);
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
