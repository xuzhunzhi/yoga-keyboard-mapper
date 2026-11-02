namespace YogaKey.Core;

public sealed class ActionPreset
{
    public ActionPreset(string name, string action)
    {
        Name = name;
        Action = action;
    }

    public string Name { get; }

    public string Action { get; }

    public string Display => $"{Name}    ({Action})";
}

public static class ActionPresets
{
    public static readonly IReadOnlyList<ActionPreset> All = new List<ActionPreset>
    {
        new("任务管理器", "ctrl+shift+esc"),
        new("锁屏", "win+l"),
        new("截图", "win+shift+s"),
        new("切换窗口", "alt+tab"),
        new("显示桌面", "win+d"),
        new("资源管理器", "win+e"),
        new("系统设置", "win+i"),
        new("复制", "ctrl+c"),
        new("粘贴", "ctrl+v"),
        new("剪切", "ctrl+x"),
        new("撤销", "ctrl+z"),
        new("计算器", "run:calc"),
        new("记事本", "run:notepad"),
    };

    public static string Describe(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return "（未设置）";
        }

        foreach (ActionPreset preset in All)
        {
            if (string.Equals(preset.Action, action, StringComparison.OrdinalIgnoreCase))
            {
                return $"{preset.Name}  ·  {action}";
            }
        }

        return action;
    }
}
