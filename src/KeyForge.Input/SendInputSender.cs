using System.Runtime.InteropServices;
using System.ComponentModel;
using KeyForge.Core.Keyboard;

namespace KeyForge.Input;

public sealed class SendInputSender : IInputSender
{
    private readonly object _gate = new();
    private readonly HashSet<string> _pressedKeys = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> PressedKeys
    {
        get
        {
            lock (_gate)
            {
                return _pressedKeys.ToList().AsReadOnly();
            }
        }
    }

    public void KeyDown(string key)
    {
        Send(key, keyUp: false);
        lock (_gate)
        {
            _pressedKeys.Add(KeyCatalog.Normalize(key));
        }
    }

    public void KeyUp(string key)
    {
        Send(key, keyUp: true);
        lock (_gate)
        {
            _pressedKeys.Remove(KeyCatalog.Normalize(key));
        }
    }

    public async Task PressAsync(string key, int holdMs = 12, CancellationToken cancellationToken = default)
    {
        KeyDown(key);
        await Task.Delay(Math.Max(1, holdMs), cancellationToken);
        KeyUp(key);
    }

    public void ReleaseAll()
    {
        List<string> keys;
        lock (_gate)
        {
            keys = _pressedKeys.Reverse().ToList();
            _pressedKeys.Clear();
        }

        foreach (var key in keys)
        {
            Send(key, keyUp: true);
        }
    }

    private static void Send(string key, bool keyUp)
    {
        if (!InputKeyMap.TryGetVirtualKey(key, out var virtualKey))
        {
            throw new InvalidOperationException($"Cannot send unknown key '{key}'.");
        }

        var flags = keyUp ? Win32KeyboardNative.KeyEventFKeyUp : 0;
        var scanCode = (ushort)Win32KeyboardNative.MapVirtualKey(virtualKey.VirtualKey, Win32KeyboardNative.MapVkToVsc);
        if (virtualKey.IsExtended)
        {
            flags |= Win32KeyboardNative.KeyEventFExtendedKey;
        }

        if (scanCode != 0)
        {
            flags |= Win32KeyboardNative.KeyEventFScancode;
        }

        var inputs = new[]
        {
            new Win32KeyboardNative.Input
            {
                Type = Win32KeyboardNative.InputKeyboard,
                U = new Win32KeyboardNative.InputUnion
                {
                    Ki = new Win32KeyboardNative.KeybdInput
                    {
                        WVk = scanCode == 0 ? virtualKey.VirtualKey : (ushort)0,
                        WScan = scanCode,
                        DwFlags = flags,
                        Time = 0,
                        DwExtraInfo = 0
                    }
                }
            }
        };

        var sent = Win32KeyboardNative.SendInput(1, inputs, Marshal.SizeOf<Win32KeyboardNative.Input>());
        if (sent != 1)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"SendInput failed for {key}.");
        }
    }
}
