namespace YogaKey.Core;

public sealed class ActionPreset
{
    public ActionPreset(string category, string name, string action)
    {
        Category = category;
        Name = name;
        Action = action;
    }

    public string Category { get; }

    public string Name { get; }

    public string Action { get; }

    public string Tooltip => $"{Category} · {Name}\n{Action}";
}

public static class ActionPresets
{
    /// <summary>Built-in actions, grouped by category and ordered as shown in the combo box.</summary>
    public static readonly IReadOnlyList<ActionPreset> All = new List<ActionPreset>
    {
        // ---- 系统 ----
        new("系统", "任务管理器", "ctrl+shift+esc"),
        new("系统", "锁屏", "win+l"),
        new("系统", "截图", "win+shift+s"),
        new("系统", "系统设置", "win+i"),
        new("系统", "资源管理器", "win+e"),
        new("系统", "显示桌面", "win+d"),
        new("系统", "任务视图", "win+tab"),
        new("系统", "运行对话框", "win+r"),
        new("系统", "剪贴板历史", "win+v"),
        new("系统", "系统信息", "run:msinfo32"),
        new("系统", "控制面板", "run:control"),
        new("系统", "设备管理器", "run:devmgmt.msc"),
        new("系统", "服务", "run:services.msc"),
        new("系统", "磁盘管理", "run:diskmgmt.msc"),
        new("系统", "注册表编辑器", "run:regedit"),

        // ---- 窗口 ----
        new("窗口", "切换窗口", "alt+tab"),
        new("窗口", "切换窗口（反向）", "alt+shift+tab"),
        new("窗口", "关闭当前窗口", "alt+f4"),
        new("窗口", "最大化", "win+up"),
        new("窗口", "最小化", "win+down"),
        new("窗口", "贴到左半屏", "win+left"),
        new("窗口", "贴到右半屏", "win+right"),

        // ---- 编辑 ----
        new("编辑", "复制", "ctrl+c"),
        new("编辑", "粘贴", "ctrl+v"),
        new("编辑", "剪切", "ctrl+x"),
        new("编辑", "撤销", "ctrl+z"),
        new("编辑", "重做", "ctrl+y"),
        new("编辑", "全选", "ctrl+a"),
        new("编辑", "保存", "ctrl+s"),
        new("编辑", "查找", "ctrl+f"),
        new("编辑", "新建", "ctrl+n"),

        // ---- 应用 ----
        new("应用", "计算器", "run:calc"),
        new("应用", "记事本", "run:notepad"),
        new("应用", "画图", "run:mspaint"),
        new("应用", "命令提示符", "run:cmd"),
        new("应用", "PowerShell", "run:powershell"),
        new("应用", "终端", "run:wt"),
        new("应用", "写字板", "run:write"),
        new("应用", "放大镜", "run:Magnify"),

        // ---- 文件 ----
        new("文件", "下载文件夹", "run:explorer.exe shell:Downloads"),
        new("文件", "桌面文件夹", "run:explorer.exe shell:Desktop"),
        new("文件", "文档文件夹", "run:explorer.exe shell:Personal"),
        new("文件", "回收站", "run:explorer.exe shell:RecycleBinFolder"),

        // ---- 媒体 ----
        new("媒体", "播放 / 暂停", "mediaplaypause"),
        new("媒体", "下一首", "medianext"),
        new("媒体", "上一首", "mediaprev"),
        new("媒体", "音量 +", "volumeup"),
        new("媒体", "音量 -", "volumedown"),
        new("媒体", "静音", "volumemute"),
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

        if (action.StartsWith("run:", StringComparison.OrdinalIgnoreCase))
        {
            return "启动  ·  " + action[4..].Trim();
        }

        if (action.StartsWith("url:", StringComparison.OrdinalIgnoreCase))
        {
            return "打开网址  ·  " + action[4..].Trim();
        }

        return action;
    }
}
