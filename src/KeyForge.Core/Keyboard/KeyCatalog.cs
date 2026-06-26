using System.Collections.ObjectModel;

namespace KeyForge.Core.Keyboard;

public static class KeyCatalog
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Esc"] = "Esc",
        ["Backquote"] = "`",
        ["Minus"] = "-",
        ["Equal"] = "=",
        ["Backspace"] = "Backspace",
        ["Tab"] = "Tab",
        ["LeftBracket"] = "[",
        ["RightBracket"] = "]",
        ["Backslash"] = "\\",
        ["CapsLock"] = "Caps",
        ["Semicolon"] = ";",
        ["Quote"] = "'",
        ["Enter"] = "Enter",
        ["LeftShift"] = "Shift",
        ["Comma"] = ",",
        ["Period"] = ".",
        ["Slash"] = "/",
        ["RightShift"] = "Shift",
        ["LeftCtrl"] = "Ctrl",
        ["LeftWin"] = "Win",
        ["LeftAlt"] = "Alt",
        ["Space"] = "Space",
        ["RightAlt"] = "Alt",
        ["RightWin"] = "Win",
        ["Menu"] = "Menu",
        ["RightCtrl"] = "Ctrl",
        ["PrintScreen"] = "PrtSc",
        ["ScrollLock"] = "ScrLk",
        ["Pause"] = "Pause",
        ["Insert"] = "Ins",
        ["Delete"] = "Del",
        ["Home"] = "Home",
        ["End"] = "End",
        ["PageUp"] = "PgUp",
        ["PageDown"] = "PgDn",
        ["Up"] = "Up",
        ["Down"] = "Down",
        ["Left"] = "Left",
        ["Right"] = "Right",
        ["NumLock"] = "Num",
        ["NumpadDivide"] = "Num /",
        ["NumpadMultiply"] = "Num *",
        ["NumpadMinus"] = "Num -",
        ["NumpadPlus"] = "Num +",
        ["NumpadEnter"] = "Enter",
        ["NumpadDecimal"] = "Num ."
    };

    private static readonly HashSet<string> ModifierKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "LeftCtrl",
        "RightCtrl",
        "LeftShift",
        "RightShift",
        "LeftAlt",
        "RightAlt",
        "LeftWin",
        "RightWin"
    };

    private static readonly HashSet<string> RiskyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "LeftCtrl",
        "RightCtrl",
        "LeftShift",
        "RightShift",
        "LeftAlt",
        "RightAlt",
        "LeftWin",
        "RightWin",
        "Esc",
        "Tab",
        "Enter"
    };

    private static readonly ReadOnlyCollection<KeyDefinition> AllKeys = BuildAllKeys().ToList().AsReadOnly();

    private static readonly Dictionary<string, KeyDefinition> Definitions =
        AllKeys.ToDictionary(key => key.Code, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<KeyDefinition> All => AllKeys;

    public static bool IsKnown(string? key) => !string.IsNullOrWhiteSpace(key) && Definitions.ContainsKey(Normalize(key));

    public static bool IsModifier(string? key) => !string.IsNullOrWhiteSpace(key) && ModifierKeys.Contains(Normalize(key));

    public static bool IsRisky(string? key) => !string.IsNullOrWhiteSpace(key) && RiskyKeys.Contains(Normalize(key));

    public static string Normalize(string key)
    {
        if (Definitions.TryGetValue(key.Trim(), out var definition))
        {
            return definition.Code;
        }

        var trimmed = key.Trim();
        if (trimmed.Length == 1 && char.IsLetter(trimmed[0]))
        {
            return char.ToUpperInvariant(trimmed[0]).ToString();
        }

        if (trimmed.Length == 1 && char.IsDigit(trimmed[0]))
        {
            return $"D{trimmed[0]}";
        }

        return trimmed;
    }

    public static string LabelFor(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var normalized = Normalize(key);
        if (Labels.TryGetValue(normalized, out var label))
        {
            return label;
        }

        if (normalized.StartsWith('D') && normalized.Length == 2 && char.IsDigit(normalized[1]))
        {
            return normalized[1].ToString();
        }

        if (normalized.StartsWith("Numpad", StringComparison.OrdinalIgnoreCase) &&
            normalized.Length == "Numpad0".Length &&
            char.IsDigit(normalized[^1]))
        {
            return $"Num {normalized[^1]}";
        }

        return normalized;
    }

    private static IEnumerable<KeyDefinition> BuildAllKeys()
    {
        foreach (var row in KeyboardLayout.Rows)
        {
            foreach (var key in row)
            {
                yield return key;
            }
        }
    }
}
