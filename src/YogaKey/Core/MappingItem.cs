namespace YogaKey.Core;

/// <summary>Row model bound by the ListView template.</summary>
public sealed class MappingItem
{
    public int Code { get; init; }

    public string Action { get; init; } = string.Empty;

    public string CodeText => FormatCode(Code);

    public string KeyLabel => $"特殊键 {CodeText}";

    public static string FormatCode(int code) => $"0x{code:X2}";

    public string ActionText => ActionPresets.Describe(Action);

    public string AccessibleText => $"{KeyLabel}，动作 {ActionText}";

    // The ListView item automation peer falls back to ToString(), so make it readable.
    public override string ToString() => AccessibleText;
}
