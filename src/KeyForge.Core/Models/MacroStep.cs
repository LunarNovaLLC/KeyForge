namespace KeyForge.Core.Models;

public sealed class MacroStep
{
    public MacroStepAction Action { get; set; }

    public string? Key { get; set; }

    public int? DelayMs { get; set; }

    public MacroStepDelayPlacement DelayPlacement { get; set; } = MacroStepDelayPlacement.After;

    public static MacroStep Press(string key) => new()
    {
        Action = MacroStepAction.Press,
        Key = key
    };

    public static MacroStep KeyDown(string key) => new()
    {
        Action = MacroStepAction.KeyDown,
        Key = key
    };

    public static MacroStep KeyUp(string key) => new()
    {
        Action = MacroStepAction.KeyUp,
        Key = key
    };

    public static MacroStep Wait(int delayMs) => new()
    {
        Action = MacroStepAction.Wait,
        DelayMs = delayMs
    };

    public MacroStep Clone() => new()
    {
        Action = Action,
        Key = Key,
        DelayMs = DelayMs,
        DelayPlacement = DelayPlacement
    };

    public override string ToString()
    {
        var timing = Action == MacroStepAction.Wait || DelayMs is not > 0
            ? string.Empty
            : $" ({DelayPlacement.ToString().ToLowerInvariant()} {DelayMs}ms)";

        return Action switch
        {
            MacroStepAction.Wait => $"Wait {DelayMs ?? 0}ms",
            MacroStepAction.KeyDown => $"Key Down: {Key ?? "?"}{timing}",
            MacroStepAction.KeyUp => $"Key Up: {Key ?? "?"}{timing}",
            _ => $"Press: {Key ?? "?"}{timing}"
        };
    }
}
