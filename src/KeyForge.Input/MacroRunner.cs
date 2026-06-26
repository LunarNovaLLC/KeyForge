using KeyForge.Core.Models;

namespace KeyForge.Input;

public sealed class MacroRunner : IMacroRunner
{
    private readonly IInputSender _inputSender;
    private readonly AppSettings _settings;

    public MacroRunner(IInputSender inputSender, AppSettings settings)
    {
        _inputSender = inputSender;
        _settings = settings;
    }

    public async Task RunAsync(IEnumerable<MacroStep> steps, CancellationToken cancellationToken = default)
    {
        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (step.Action)
            {
                case MacroStepAction.KeyDown:
                    if (!string.IsNullOrWhiteSpace(step.Key))
                    {
                        _inputSender.KeyDown(step.Key);
                    }

                    break;
                case MacroStepAction.KeyUp:
                    if (!string.IsNullOrWhiteSpace(step.Key))
                    {
                        _inputSender.KeyUp(step.Key);
                    }

                    break;
                case MacroStepAction.Wait:
                    await Task.Delay(Math.Max(_settings.MacroMinimumDelayMs, step.DelayMs ?? _settings.MacroMinimumDelayMs),
                        cancellationToken);
                    break;
                case MacroStepAction.Press:
                default:
                    if (!string.IsNullOrWhiteSpace(step.Key))
                    {
                        await _inputSender.PressAsync(step.Key, cancellationToken: cancellationToken);
                    }

                    break;
            }
        }
    }
}
