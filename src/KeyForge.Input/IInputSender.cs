namespace KeyForge.Input;

public interface IInputSender
{
    IReadOnlyCollection<string> PressedKeys { get; }

    void KeyDown(string key);

    void KeyUp(string key);

    Task PressAsync(string key, int holdMs = 12, CancellationToken cancellationToken = default);

    void ReleaseAll();
}
