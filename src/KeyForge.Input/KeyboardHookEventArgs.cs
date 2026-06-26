namespace KeyForge.Input;

public sealed class KeyboardHookEventArgs : EventArgs
{
    public KeyboardHookEventArgs(string key, bool isKeyDown, bool isInjected, int virtualKeyCode)
    {
        Key = key;
        IsKeyDown = isKeyDown;
        IsInjected = isInjected;
        VirtualKeyCode = virtualKeyCode;
    }

    public string Key { get; }

    public bool IsKeyDown { get; }

    public bool IsKeyUp => !IsKeyDown;

    public bool IsInjected { get; }

    public int VirtualKeyCode { get; }

    public bool Suppress { get; set; }
}
