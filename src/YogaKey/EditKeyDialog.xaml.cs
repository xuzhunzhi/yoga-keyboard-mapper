using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using YogaKey.Core;

namespace YogaKey;

public sealed partial class EditKeyDialog : ContentDialog
{
    private readonly DispatcherTimer _captureTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private bool _syncingAction;

    public EditKeyDialog(int? code, string? action)
    {
        InitializeComponent();
        _captureTimer.Tick += OnCaptureTimeout;

        Title = code is null ? "新增映射" : "编辑映射";

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
        CaptureButton.Content = "重新捕获";
        IsCapturing = false;
        ErrorBar.IsOpen = false;
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
        CaptureButton.Content = "取消捕获";
        CodeText.Text = "等待按键（8 秒）";
        ErrorBar.IsOpen = false;
        _captureTimer.Stop();
        _captureTimer.Start();
        CaptureRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CancelCapture()
    {
        _captureTimer.Stop();
        IsCapturing = false;
        CaptureButton.Content = Code.HasValue ? "重新捕获" : "开始捕获";
        CodeText.Text = Code.HasValue ? $"0x{Code:X2}" : "尚未捕获";
    }

    private void OnCaptureTimeout(object? sender, object e)
    {
        _captureTimer.Stop();
        if (!IsCapturing)
        {
            return;
        }

        CancelCapture();
        ShowError("没有收到按键。请确认 Yoga Book 键盘已连接后重试。");
    }

    private void OnActionTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingAction)
        {
            return;
        }

        // Keep the preset combo in sync when the user edits the action by hand.
        _syncingAction = true;
        try
        {
            string text = ActionBox.Text.Trim();
            int index = -1;
            for (int i = 0; i < ActionPresets.All.Count; i++)
            {
                if (string.Equals(ActionPresets.All[i].Action, text, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            PresetCombo.SelectedIndex = index;
        }
        finally
        {
            _syncingAction = false;
        }
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingAction)
        {
            return;
        }

        if (PresetCombo.SelectedItem is ActionPreset preset)
        {
            _syncingAction = true;
            try
            {
                ActionBox.Text = preset.Action;
            }
            finally
            {
                _syncingAction = false;
            }
        }
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // The primary button stays enabled on purpose: pressing it is the only way
        // the user gets told *why* the dialog cannot be saved yet.
        if (Code is null)
        {
            args.Cancel = true;
            ShowError("请先点「开始捕获」，再按一下键盘上的特殊键。");
            CaptureButton.Focus(FocusState.Programmatic);
            return;
        }

        if (Action.Length == 0)
        {
            args.Cancel = true;
            ShowError("请选择或填写一个动作。");
            ActionBox.Focus(FocusState.Programmatic);
        }
    }

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
