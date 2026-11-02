using System.Diagnostics;
using System.Runtime.InteropServices;

namespace YogaKey.Core;

/// <summary>
/// Turns an action string ("ctrl+shift+esc", "win+l", "run:notepad") into real
/// key strokes or a launched process.
/// </summary>
public static class KeySender
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public static bool Execute(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return false;
        }

        action = action.Trim();

        if (action.StartsWith("run:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Process.Start(new ProcessStartInfo(action[4..].Trim()) { UseShellExecute = true });
                return true;
            }
            catch
            {
                return false;
            }
        }

        var keys = new List<ushort>();
        foreach (string part in action.Split('+'))
        {
            string token = part.Trim();
            if (token.Length == 0)
            {
                continue;
            }

            ushort vk = VirtualKeyFromName(token);
            if (vk == 0)
            {
                return false;
            }

            keys.Add(vk);
        }

        if (keys.Count == 0)
        {
            return false;
        }

        // The whole chord goes out in ONE SendInput call on purpose. When the key-down
        // and key-up batches were separate calls, anything that dropped or blocked the
        // second call (app closing, UIPI, a lock screen) left the modifier logically
        // held down - and then every normal key behaves like a Win/Ctrl shortcut.
        var inputs = new INPUT[keys.Count * 2];
        for (int i = 0; i < keys.Count; i++)
        {
            inputs[i] = MakeInput(keys[i], false);
            inputs[inputs.Length - 1 - i] = MakeInput(keys[i], true);
        }

        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return sent == inputs.Length;
    }

    /// <summary>
    /// Safety net: release every modifier key. Called on startup and shutdown so a
    /// crash or a killed process can never leave the user typing with a stuck Win key.
    /// </summary>
    public static void ReleaseModifiers()
    {
        ushort[] modifiers = { 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 };
        var inputs = new INPUT[modifiers.Length];
        for (int i = 0; i < modifiers.Length; i++)
        {
            inputs[i] = MakeInput(modifiers[i], true);
        }

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT MakeInput(ushort vk, bool keyUp)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                },
            },
        };
    }

    public static ushort VirtualKeyFromName(string name)
    {
        string s = name.ToLowerInvariant();

        if (s.Length == 1)
        {
            char c = s[0];
            if (c >= 'a' && c <= 'z') return (ushort)(0x41 + (c - 'a'));
            if (c >= '0' && c <= '9') return (ushort)(0x30 + (c - '0'));
        }

        if (s.Length >= 2 && s[0] == 'f' && ushort.TryParse(s[1..], out ushort fnum) && fnum >= 1 && fnum <= 24)
        {
            return (ushort)(0x6F + fnum);
        }

        return s switch
        {
            "win" or "lwin" => 0x5B,
            "rwin" => 0x5C,
            "ctrl" or "control" or "lctrl" => 0xA2,
            "rctrl" => 0xA3,
            "alt" or "lalt" or "menu" => 0xA4,
            "ralt" => 0xA5,
            "shift" or "lshift" => 0xA0,
            "rshift" => 0xA1,
            "esc" or "escape" => 0x1B,
            "tab" => 0x09,
            "space" => 0x20,
            "enter" or "return" => 0x0D,
            "backspace" or "back" => 0x08,
            "delete" or "del" => 0x2E,
            "insert" or "ins" => 0x2D,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "pgup" => 0x21,
            "pagedown" or "pgdn" => 0x22,
            "left" => 0x25,
            "up" => 0x26,
            "right" => 0x27,
            "down" => 0x28,
            "printscreen" or "prtsc" => 0x2C,
            "capslock" => 0x14,
            "volumeup" => 0xAF,
            "volumedown" => 0xAE,
            "volumemute" => 0xAD,
            "mediaplaypause" => 0xB3,
            "medianext" => 0xB0,
            "mediaprev" => 0xB1,
            "sleep" => 0x5F,
            _ => 0,
        };
    }
}
