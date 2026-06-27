using KeyForge.Core.Keyboard;
using KeyForge.Core.Models;

namespace KeyForge.Core.Services;

public static class BindingFormatter
{
    public static string Format(KeyBinding? binding)
    {
        if (binding is null)
        {
            return "Default";
        }

        if (binding.Type == BindingType.Disabled)
        {
            return "Disabled";
        }

        if (binding.Output.Count == 0)
        {
            return "Unassigned";
        }

        if (binding.Type == BindingType.Macro)
        {
            return string.Join(", ", binding.Output.Select(FormatStep));
        }

        var parts = new List<string>();
        foreach (var step in binding.Output)
        {
            if (step.Action == MacroStepAction.Press && !string.IsNullOrWhiteSpace(step.Key))
            {
                parts.Add(KeyCatalog.LabelFor(step.Key));
            }
            else if (step.Action == MacroStepAction.KeyDown && !string.IsNullOrWhiteSpace(step.Key))
            {
                parts.Add(KeyCatalog.LabelFor(step.Key));
            }
        }

        return parts.Count == 0 ? string.Join(", ", binding.Output.Select(FormatStep)) : string.Join(" + ", parts);
    }

    public static string FormatStep(MacroStep step)
    {
        var formatted = step.Action switch
        {
            MacroStepAction.Wait => $"wait {step.DelayMs ?? 0}ms",
            MacroStepAction.KeyDown => $"{KeyCatalog.LabelFor(step.Key)} down",
            MacroStepAction.KeyUp => $"{KeyCatalog.LabelFor(step.Key)} up",
            _ => KeyCatalog.LabelFor(step.Key)
        };

        if (step.Action != MacroStepAction.Wait && step.DelayMs is > 0)
        {
            var placement = step.DelayPlacement == MacroStepDelayPlacement.Before ? "before" : "after";
            formatted = $"{formatted} ({placement} {step.DelayMs}ms)";
        }

        return formatted;
    }
}
