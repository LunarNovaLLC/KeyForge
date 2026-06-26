namespace KeyForge.Input;

public interface IKeyboardHook : IDisposable
{
    event EventHandler<KeyboardHookEventArgs>? KeyEvent;

    bool IsRunning { get; }

    void Start();

    void Stop();
}
