using KeyForge.Core.Models;

namespace KeyForge.Input;

public interface IMacroRunner
{
    Task RunAsync(IEnumerable<MacroStep> steps, CancellationToken cancellationToken = default);
}
