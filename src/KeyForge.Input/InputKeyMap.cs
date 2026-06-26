using KeyForge.Core.Keyboard;

namespace KeyForge.Input;

public readonly record struct VirtualKeyInfo(ushort VirtualKey, bool IsExtended);

public static class InputKeyMap
{
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkReturn = 0x0D;
    private const uint LlkhfExtended = 0x01;

    private static readonly Dictionary<string, VirtualKeyInfo> KeyToVirtual = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Esc"] = new(0x1B, false),
        ["Backquote"] = new(0xC0, false),
        ["Minus"] = new(0xBD, false),
        ["Equal"] = new(0xBB, false),
        ["Backspace"] = new(0x08, false),
        ["Tab"] = new(0x09, false),
        ["LeftBracket"] = new(0xDB, false),
        ["RightBracket"] = new(0xDD, false),
        ["Backslash"] = new(0xDC, false),
        ["CapsLock"] = new(0x14, false),
        ["Semicolon"] = new(0xBA, false),
        ["Quote"] = new(0xDE, false),
        ["Enter"] = new(0x0D, false),
        ["LeftShift"] = new(0xA0, false),
        ["Comma"] = new(0xBC, false),
        ["Period"] = new(0xBE, false),
        ["Slash"] = new(0xBF, false),
        ["RightShift"] = new(0xA1, false),
        ["LeftCtrl"] = new(0xA2, false),
        ["LeftWin"] = new(0x5B, true),
        ["LeftAlt"] = new(0xA4, false),
        ["Space"] = new(0x20, false),
        ["RightAlt"] = new(0xA5, true),
        ["RightWin"] = new(0x5C, true),
        ["Menu"] = new(0x5D, true),
        ["RightCtrl"] = new(0xA3, true),
        ["PrintScreen"] = new(0x2C, true),
        ["ScrollLock"] = new(0x91, false),
        ["Pause"] = new(0x13, false),
        ["Insert"] = new(0x2D, true),
        ["Delete"] = new(0x2E, true),
        ["Home"] = new(0x24, true),
        ["End"] = new(0x23, true),
        ["PageUp"] = new(0x21, true),
        ["PageDown"] = new(0x22, true),
        ["Up"] = new(0x26, true),
        ["Down"] = new(0x28, true),
        ["Left"] = new(0x25, true),
        ["Right"] = new(0x27, true),
        ["NumLock"] = new(0x90, false),
        ["NumpadDivide"] = new(0x6F, true),
        ["NumpadMultiply"] = new(0x6A, false),
        ["NumpadMinus"] = new(0x6D, false),
        ["NumpadPlus"] = new(0x6B, false),
        ["NumpadEnter"] = new(0x0D, true),
        ["NumpadDecimal"] = new(0x6E, false)
    };

    private static readonly Dictionary<ushort, string> VirtualToKey = new()
    {
        [0x1B] = "Esc",
        [0xC0] = "Backquote",
        [0xBD] = "Minus",
        [0xBB] = "Equal",
        [0x08] = "Backspace",
        [0x09] = "Tab",
        [0xDB] = "LeftBracket",
        [0xDD] = "RightBracket",
        [0xDC] = "Backslash",
        [0x14] = "CapsLock",
        [0xBA] = "Semicolon",
        [0xDE] = "Quote",
        [0x0D] = "Enter",
        [0xBC] = "Comma",
        [0xBE] = "Period",
        [0xBF] = "Slash",
        [0x5B] = "LeftWin",
        [0x5C] = "RightWin",
        [0x5D] = "Menu",
        [0x20] = "Space",
        [0x2C] = "PrintScreen",
        [0x91] = "ScrollLock",
        [0x13] = "Pause",
        [0x2D] = "Insert",
        [0x2E] = "Delete",
        [0x24] = "Home",
        [0x23] = "End",
        [0x21] = "PageUp",
        [0x22] = "PageDown",
        [0x26] = "Up",
        [0x28] = "Down",
        [0x25] = "Left",
        [0x27] = "Right",
        [0x90] = "NumLock",
        [0x6F] = "NumpadDivide",
        [0x6A] = "NumpadMultiply",
        [0x6D] = "NumpadMinus",
        [0x6B] = "NumpadPlus",
        [0x6E] = "NumpadDecimal"
    };

    static InputKeyMap()
    {
        for (var ch = 'A'; ch <= 'Z'; ch++)
        {
            KeyToVirtual[ch.ToString()] = new((ushort)ch, false);
            VirtualToKey[(ushort)ch] = ch.ToString();
        }

        for (var digit = 0; digit <= 9; digit++)
        {
            KeyToVirtual[$"D{digit}"] = new((ushort)('0' + digit), false);
            KeyToVirtual[$"Numpad{digit}"] = new((ushort)(0x60 + digit), false);
            VirtualToKey[(ushort)('0' + digit)] = $"D{digit}";
            VirtualToKey[(ushort)(0x60 + digit)] = $"Numpad{digit}";
        }

        for (var index = 1; index <= 12; index++)
        {
            KeyToVirtual[$"F{index}"] = new((ushort)(0x6F + index), false);
            VirtualToKey[(ushort)(0x6F + index)] = $"F{index}";
        }
    }

    public static bool TryGetVirtualKey(string key, out VirtualKeyInfo virtualKey)
    {
        return KeyToVirtual.TryGetValue(KeyCatalog.Normalize(key), out virtualKey);
    }

    public static string FromVirtualKey(int virtualKey, int scanCode, uint flags)
    {
        var vk = (ushort)virtualKey;
        var extended = (flags & LlkhfExtended) == LlkhfExtended;

        return virtualKey switch
        {
            0xA0 => "LeftShift",
            0xA1 => "RightShift",
            0xA2 => "LeftCtrl",
            0xA3 => "RightCtrl",
            0xA4 => "LeftAlt",
            0xA5 => "RightAlt",
            VkShift => scanCode == 0x36 ? "RightShift" : "LeftShift",
            VkControl => extended ? "RightCtrl" : "LeftCtrl",
            VkMenu => extended ? "RightAlt" : "LeftAlt",
            VkReturn => extended ? "NumpadEnter" : "Enter",
            _ => VirtualToKey.TryGetValue(vk, out var key) ? key : $"VK_{virtualKey:X2}"
        };
    }
}
