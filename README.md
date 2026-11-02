# YogaKey

> 仓库：`yoga-keyboard-mapper` · 可执行文件：`YogaKey.exe`

把 **Lenovo Yoga Book 9 蓝牙键盘**上那些「特殊键」（Copilot / 联想 / 星形 / 性能控制台等）重新映射成你想要的快捷键。

Windows 上通常没人能改这些键 —— 因为它们**根本不产生按键事件**。

![screenshot](docs/screenshot.png)

## 为什么普通工具改不了

Yoga Book 9 的蓝牙键盘对这几颗键发出的是**厂商自定义的 HID 报告**，而不是标准键盘 Usage：

```
报告 ID = 0x03
按下：[03 10 00 XX]      ← XX 是厂商键码
松开：[03 00 00 00]
```

Windows 的键盘栈完全不认它，所以：

- ❌ PowerToys 键盘管理器 —— 收不到按键，无法重映射
- ❌ 注册表 ScanCode Map / SharpKeys —— 没有扫描码可映射
- ❌ AutoHotkey 的普通热键 —— 同样收不到

YogaKey 直接以 **Raw Input** 读取这份 HID 报告，拿到厂商键码后再用 `SendInput` 合成你指定的快捷键。

## 功能

- 🪟 **原生 WinUI 3 界面**：Windows 11 风格（Mica、SettingsCard、ContentDialog），支持浅色/深色主题
- ⌨️ 快捷键：`Ctrl+N` 新增、`F2` 编辑、`Delete` 删除
- 🔍 **捕获**：按一下键盘上的特殊键，自动读出它的厂商键码（如 `0x12`）
- 🧩 **映射**：每个键码绑定一个动作，预置任务管理器 / 锁屏 / 截图 / 切窗口 / 复制粘贴等
- ⌨️ **自定义**：任意组合键（`ctrl+shift+t`）或启动程序（`run:calc`）
- 🚀 **开机自启**：勾选即可写注册表 Run 项
- 📄 配置为纯 JSON：`%LOCALAPPDATA%\YogaKey\config.json`

## 构建

需要：

- Windows 10 1809+ / Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Windows App SDK 1.8 运行时](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)

```powershell
git clone https://github.com/<you>/yoga-keyboard-mapper.git
cd yoga-keyboard-mapper
dotnet build src\YogaKey\YogaKey.csproj -c Release -p:Platform=x64
```

产物在：

```
src\YogaKey\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\YogaKey.exe
```

## 使用

1. 启动 `YogaKey.exe`
2. 点击 **开始捕获**，按一下键盘上那颗要改的键 → 显示 `0x12` 之类的键码
3. 在「常用动作」里选一个，或直接在「动作」里填组合键
4. **保存映射**
5. 保持窗口开启（可以最小化）即可生效；想要开机就有，勾选 **开机自启**

> 关闭窗口 = 停止监听。

## 动作语法

| 写法 | 含义 |
| --- | --- |
| `ctrl+shift+esc` | 组合键（任意顺序，`+` 分隔） |
| `win+l` | 徽标键组合 |
| `alt+tab` / `win+shift+s` | 常用系统快捷键 |
| `f13` … `f24` | 功能键 |
| `run:notepad` | 启动程序（后接可执行文件名或完整路径） |

可用键名：字母 `a`–`z`、数字 `0`–`9`、`f1`–`f24`、`win` `ctrl` `alt` `shift`（可加 `l`/`r` 前缀）、`esc` `tab` `space` `enter` `backspace` `delete` `insert` `home` `end` `pageup` `pagedown` `left` `up` `right` `down` `printscreen` `capslock`、媒体键 `volumeup` `volumedown` `volumemute` `mediaplaypause` `medianext` `mediaprev`。

## 配置

`%LOCALAPPDATA%\YogaKey\config.json`：

```json
{
  "DeviceMatch": "FE5A42B74943",
  "CollectionMatch": "Col05",
  "StartWithWindows": true,
  "Mappings": [
    { "Code": 18, "Action": "ctrl+shift+esc" }
  ]
}
```

- `DeviceMatch`：原始输入设备路径里的子串，用来锁定是哪块键盘。换键盘 / 重新配对后如果失效，把它改成新实例 ID 即可；留空表示接受任何设备。
- `CollectionMatch`：HID 集合标识，Yoga Book 9 是 `Col05`，留空同上。
- `Code`：十进制键码（`0x12` = 18）。

改完 JSON 后重启程序，或在界面里重新保存一次。

## 排错

- **按了没反应**：点「开始捕获」再按，看是否出现键码。没有键码说明这颗键不是通过 HID 报告发出的，YogaKey 帮不了。
- **任务管理器没弹出，但日志有记录**：前台窗口可能以管理员权限运行，Windows 的 UIPI 会拦掉 `SendInput`。
- 日志：`%LOCALAPPDATA%\YogaKey\app.log`

---

## English

YogaKey remaps the vendor-specific keys of the **Lenovo Yoga Book 9 Bluetooth keyboard** on Windows.

Those keys never reach the normal keyboard stack — they arrive as vendor-defined HID reports (`[03 10 00 XX]`), so PowerToys / SharpKeys / AutoHotkey cannot see them. YogaKey reads the raw input report, takes the vendor key code and synthesises a real shortcut with `SendInput`.

```powershell
dotnet build src\YogaKey\YogaKey.csproj -c Release -p:Platform=x64
```

MIT licensed.
