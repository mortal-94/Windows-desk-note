using System.Globalization;

namespace DeskNote.Core.Input;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
    NoRepeat = 0x4000,
}

/// <summary>Platform-neutral representation of a RegisterHotKey gesture.</summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, uint VirtualKey, string KeyName)
{
    private static readonly Dictionary<string, uint> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["End"] = 0x23,
        ["Home"] = 0x24,
        ["Insert"] = 0x2D,
        ["Delete"] = 0x2E,
    };

    public static HotkeyGesture Default { get; } = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'N', "N");

    public static bool TryParse(string? value, out HotkeyGesture gesture, out string error)
    {
        gesture = default;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "快捷键不能为空。";
            return false;
        }

        string[] parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        HotkeyModifiers modifiers = HotkeyModifiers.None;
        string? keyPart = null;

        foreach (string part in parts)
        {
            HotkeyModifiers modifier = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifiers.Control,
                "ALT" => HotkeyModifiers.Alt,
                "SHIFT" => HotkeyModifiers.Shift,
                "WIN" or "WINDOWS" => HotkeyModifiers.Windows,
                _ => HotkeyModifiers.None,
            };

            if (modifier != HotkeyModifiers.None)
            {
                if ((modifiers & modifier) != 0)
                {
                    error = $"修饰键 {part} 重复。";
                    return false;
                }

                modifiers |= modifier;
            }
            else if (keyPart is null)
            {
                keyPart = part;
            }
            else
            {
                error = "快捷键只能包含一个普通按键。";
                return false;
            }
        }

        // Shift+letter would steal ordinary capital-letter input system-wide.
        if ((modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Windows)) == 0)
        {
            error = "请至少使用 Ctrl、Alt 或 Win 中的一个修饰键。";
            return false;
        }

        if (keyPart is null || !TryGetVirtualKey(keyPart, out uint virtualKey, out string canonicalKey))
        {
            error = "普通按键可使用 A-Z、0-9、F1-F24（F12 除外）、Space、Home、End、Insert、Delete、PageUp 或 PageDown。";
            return false;
        }

        if (virtualKey == 0x7B) // F12 is permanently reserved for the debugger by Windows.
        {
            error = "F12 是 Windows 调试器保留键，请选择其他按键。";
            return false;
        }

        gesture = new HotkeyGesture(modifiers, virtualKey, canonicalKey);
        return true;
    }

    public override string ToString()
    {
        List<string> parts = [];
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        parts.Add(KeyName);
        return string.Join('+', parts);
    }

    private static bool TryGetVirtualKey(string key, out uint virtualKey, out string canonical)
    {
        string normalized = key.Trim();
        if (normalized.Length == 1 && char.IsAsciiLetterOrDigit(normalized[0]))
        {
            char upper = char.ToUpperInvariant(normalized[0]);
            virtualKey = upper;
            canonical = upper.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (normalized.Length is 2 or 3 && normalized[0] is 'F' or 'f' &&
            int.TryParse(normalized.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + functionKey - 1);
            canonical = $"F{functionKey}";
            return true;
        }

        if (NamedKeys.TryGetValue(normalized, out virtualKey))
        {
            canonical = NamedKeys.Keys.First(name => name.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            return true;
        }

        canonical = string.Empty;
        return false;
    }
}
