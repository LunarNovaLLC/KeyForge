namespace KeyForge.Core.Models;

public sealed class KeyBinding
{
    public string Input { get; set; } = string.Empty;

    public BindingType Type { get; set; } = BindingType.Simple;

    public bool BlockOriginal { get; set; } = true;

    public bool RepeatWhileHeld { get; set; }

    public List<MacroStep> Output { get; set; } = [];

    public KeyBinding Clone() => new()
    {
        Input = Input,
        Type = Type,
        BlockOriginal = BlockOriginal,
        RepeatWhileHeld = RepeatWhileHeld,
        Output = Output.Select(step => step.Clone()).ToList()
    };
}
