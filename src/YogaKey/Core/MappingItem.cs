namespace YogaKey.Core;

/// <summary>Row model bound by the ListView template.</summary>
public sealed class MappingItem
{
    public int Code { get; init; }

    public string Action { get; init; } = string.Empty;

    public string CodeText => $"0x{Code:X2}";

    public string ActionText => ActionPresets.Describe(Action);
}
