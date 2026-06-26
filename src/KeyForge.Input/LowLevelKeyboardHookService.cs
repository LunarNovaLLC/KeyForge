using System.ComponentModel;
using System.Runtime.InteropServices;

namespace KeyForge.Input;

public sealed class LowLevelKeyboardHookService : IKeyboardHook
{
    private readonly Win32KeyboardNative.LowLevelKeyboardProc _callback;
    private nint _hookHandle;
    private bool _disposed;

    public LowLevelKeyboardHookService()
    {
        _callback = HookCallback;
    }

    public event EventHandler<KeyboardHookEventArgs>? KeyEvent;

    public bool IsRunning => _hookHandle != 0;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning)
        {
            return;
        }

        _hookHandle = Win32KeyboardNative.SetWindowsHookEx(
            Win32KeyboardNative.WhKeyboardLl,
            _callback,
            Win32KeyboardNative.GetModuleHandle(null),
            0);

        if (_hookHandle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to install the keyboard hook.");
        }
    }

    public void Stop()
    {
        if (_hookHandle == 0)
        {
            return;
        }

        if (!Win32KeyboardNative.UnhookWindowsHookEx(_hookHandle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to remove the keyboard hook.");
        }

        _hookHandle = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_hookHandle != 0)
        {
            Win32KeyboardNative.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = 0;
        }

        _disposed = true;
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0)
        {
            return Win32KeyboardNative.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var message = wParam.ToInt32();
        var isDown = message is Win32KeyboardNative.WmKeyDown or Win32KeyboardNative.WmSysKeyDown;
        var isUp = message is Win32KeyboardNative.WmKeyUp or Win32KeyboardNative.WmSysKeyUp;
        if (!isDown && !isUp)
        {
            return Win32KeyboardNative.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<Win32KeyboardNative.KbdLlHookStruct>(lParam);
        var injected = (data.Flags & (Win32KeyboardNative.LlkhfInjected | Win32KeyboardNative.LlkhfLowerIlInjected)) != 0;
        var key = InputKeyMap.FromVirtualKey((int)data.VkCode, (int)data.ScanCode, data.Flags);
        var args = new KeyboardHookEventArgs(key, isDown, injected, (int)data.VkCode);
        try
        {
            KeyEvent?.Invoke(this, args);
        }
        catch
        {
            args.Suppress = false;
        }

        return args.Suppress ? 1 : Win32KeyboardNative.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }
}
