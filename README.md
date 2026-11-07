# Yoga Keyboard Mapper

把 **Lenovo Yoga Book 9 蓝牙键盘**上那些「特殊键」（联想 / 星形 / 性能控制台等）重新映射成你想要的快捷键。

> **Copilot 键不在支持范围内**：它发出的是标准组合键 `Win+Shift+F23`，不走厂商 HID 报告，本程序拦不到。想改它可以用 Windows 11 的「设置 → 个性化 → 文本输入 → 自定义键盘上的 Copilot 键」，或 PowerToys 键盘管理器。

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

Yoga Keyboard Mapper 直接以 **Raw Input** 读取这份 HID 报告，拿到厂商键码后再用 `SendInput` 合成你指定的快捷键。

## 功能

- 🪟 **原生 WinUI 3 界面**：Windows 11 风格（Mica、SettingsCard、ContentDialog），支持浅色/深色主题
- 🌐 **界面语言**：支持简体中文和 English，自动跟随 Windows 显示语言
- ⌨️ 快捷键：`Ctrl+N` 新增、`F2` 编辑、`Delete` 删除
- 🔍 **捕获**：按一下键盘上的特殊键，自动读出它的厂商键码（如 `0x12`）
- 🧩 **映射**：每个键码绑定一个动作，内置 **49 个预置动作**，按 系统 / 窗口 / 编辑 / 应用 / 文件 / 媒体 分组
- ⌨️ **自定义**：任意组合键（`ctrl+shift+t`）、启动任意程序（点「选择程序…」直接挑 `.exe`，或写 `run:"C:\Program Files\App\App.exe"`）、打开网址（`url:https://…`）
- 🚀 **开机自启**：一键开启，登录后以最小化启动
- 📄 配置为纯 JSON：`%LOCALAPPDATA%\YogaKeyboardMapper\config.json`

## 下载安装

到 [Releases](https://github.com/xuzhunzhi/yoga-keyboard-mapper/releases) 下载，任选一种（都是 x64，已自带 .NET 和 Windows App SDK 运行时，无需另装）：

| 文件 | 说明 |
| --- | --- |
| `YogaKeyboardMapper-<版本>-x64-setup.exe` | **推荐**。安装到当前用户，不需要管理员权限，带开始菜单快捷方式和卸载项 |
| `YogaKeyboardMapper-<版本>-x64.zip` / `.7z` | 便携版，解压后运行 `YogaKeyboardMapper.exe` |
| `YogaKeyboardMapper-<版本>-x64.msix` + `YogaKeyboardMapper.cer` | MSIX 包（自签名），需要先信任证书，见下 |

> exe 没有购买代码签名证书，首次运行时 SmartScreen 可能提示「Windows 已保护你的电脑」，点 **更多信息 → 仍要运行** 即可。可用 `SHA256SUMS.txt` 校验文件。

**安装 MSIX**：先以管理员身份把证书导入「受信任的人」，再双击 `.msix` 安装：

```powershell
Import-Certificate -FilePath .\YogaKeyboardMapper.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

（或右键 `.cer` → 安装证书 → 本地计算机 → 将所有的证书都放入下列存储 → 受信任的人。）

MSIX 版的配置保存在 `%LOCALAPPDATA%\Packages\xuzhunzhi.YogaKeyboardMapper_<id>\LocalState\`，首次启动会自动复制 exe/zip 版已有的配置；开机自启用的是系统的「启动应用」，可在 **设置 → 应用 → 启动** 里管理。

## 构建

需要：

- Windows 10 1809+ / Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Windows App SDK 1.8 运行时](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)

```powershell
git clone https://github.com/xuzhunzhi/yoga-keyboard-mapper.git
cd yoga-keyboard-mapper
dotnet build src\YogaKeyboardMapper\YogaKeyboardMapper.csproj -c Release -p:Platform=x64
```

产物在：

```
src\YogaKeyboardMapper\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\YogaKeyboardMapper.exe
```

### 打包发布文件

另需 [Inno Setup 6](https://jrsoftware.org/isinfo.php)（`winget install JRSoftware.InnoSetup`）和 7-Zip / NanaZip：

```powershell
powershell -ExecutionPolicy Bypass -File tools\package.ps1 -NewCertificate   # 第一次：生成自签名证书
powershell -ExecutionPolicy Bypass -File tools\package.ps1
```

输出到 `artifacts\release\`。版本号取自 `.csproj` 的 `<Version>`。加上 `-StoreIdentityName`、`-StorePublisher`、`-StorePublisherDisplayName`（Partner Center「产品标识」页上的值）会再生成一个未签名的 `*-store.msix` 用于提交 Microsoft Store。

## 使用

1. 启动 `YogaKeyboardMapper.exe`
2. 点击 **开始捕获**，按一下键盘上那颗要改的键 → 显示 `0x12` 之类的键码
3. 在「常用动作」里选一个，或直接在「动作」里填组合键
4. **保存映射**
5. 保持窗口开启（可以最小化）即可生效；想要开机就有，勾选 **开机自启**

> 关闭窗口 = 停止监听。程序已在运行时再次启动 `YogaKeyboardMapper.exe`，会把已有窗口调到前台。

## 动作语法

| 写法 | 含义 |
| --- | --- |
| `ctrl+shift+esc` | 组合键（任意顺序，`+` 分隔） |
| `win+l` | 徽标键组合 |
| `alt+tab` / `win+shift+s` | 常用系统快捷键 |
| `f13` … `f24` | 功能键 |
| `run:notepad` | 启动程序（可执行文件名，或 `shell:` 别名） |
| `run:"C:\Program Files\App\App.exe" --flag` | 带引号的完整路径 + 参数 |
| `run:explorer.exe shell:Downloads` | 程序 + 参数 |
| `url:https://example.com` | 用默认浏览器打开网址 |

可用键名：字母 `a`–`z`、数字 `0`–`9`、`f1`–`f24`、`win` `ctrl` `alt` `shift`（可加 `l`/`r` 前缀）、`esc` `tab` `space` `enter` `backspace` `delete` `insert` `home` `end` `pageup` `pagedown` `left` `up` `right` `down` `printscreen` `capslock`、媒体键 `volumeup` `volumedown` `volumemute` `mediaplaypause` `medianext` `mediaprev`。

## 配置

`%LOCALAPPDATA%\YogaKeyboardMapper\config.json`：

> 旧版本叫 YogaKey，配置在 `%LOCALAPPDATA%\YogaKey\`。新版首次启动会自动把 `config.json` 复制过来，开机自启项也会自动换成新名字；确认无误后可以删掉旧文件夹。

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

- **按了没反应**：点「开始捕获」再按，看是否出现键码。没有键码说明这颗键不是通过 HID 报告发出的，本程序帮不了。
- **任务管理器没弹出，但日志有记录**：前台窗口可能以管理员权限运行，Windows 的 UIPI 会拦掉 `SendInput`。
- **普通按键变得像按着什么 Win/Ctrl 快捷键**：说明修饰键被逻辑卡住了（程序在按键按下与抬起之间被关闭就会这样）。按一下对应的修饰键即可恢复；1.0 起整段组合键用**一次** `SendInput` 发出，且启动/退出时会主动释放所有修饰键。
- **映射突然全没了**：`config.json` 无法解析时，程序会把它改名为 `config.json.bad-<时间>` 再使用默认设置，修好后改回 `config.json` 即可。
- 日志：`%LOCALAPPDATA%\YogaKeyboardMapper\app.log`

---

## English

Yoga Keyboard Mapper remaps the vendor-specific keys of the **Lenovo Yoga Book 9 Bluetooth keyboard** on Windows.

Those keys never reach the normal keyboard stack — they arrive as vendor-defined HID reports (`[03 10 00 XX]`), so PowerToys / SharpKeys / AutoHotkey cannot see them. Yoga Keyboard Mapper reads the raw input report, takes the vendor key code and synthesises a real shortcut with `SendInput`.

**Download** from [Releases](https://github.com/xuzhunzhi/yoga-keyboard-mapper/releases) (x64, self-contained, no runtime needed):

- `*-setup.exe`: per-user installer, no admin rights needed (recommended)
- `*.zip` / `*.7z`: portable
- `*.msix` + `YogaKeyboardMapper.cer`: self-signed MSIX. First trust the certificate from an elevated PowerShell with `Import-Certificate -FilePath .\YogaKeyboardMapper.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople`, then open the `.msix`.

The files aren't code-signed, so SmartScreen may warn on first run: choose **More info → Run anyway**.

```powershell
dotnet build src\YogaKeyboardMapper\YogaKeyboardMapper.csproj -c Release -p:Platform=x64
powershell -ExecutionPolicy Bypass -File tools\package.ps1   # release files (needs Inno Setup 6 and 7-Zip)
```

MIT licensed.
