using KeyForge.Core.Keyboard;
using KeyForge.Core.Models;

namespace KeyForge.Core.Validation;

public sealed class ProfileValidator
{
    public const int MaxMacroSteps = 10;
    public const int MaxMacroDelayMs = 5000;

    private readonly AppSettings _settings;
    private readonly Func<string, bool>? _exeExists;

    public ProfileValidator(AppSettings? settings = null, Func<string, bool>? exeExists = null)
    {
        _settings = settings ?? new AppSettings();
        _exeExists = exeExists;
    }

    public ValidationResult Validate(KeyForgeProfile profile)
    {
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(profile.ProfileId))
        {
            result.Add(ValidationSeverity.Error, "Profile ID is required.", nameof(profile.ProfileId));
        }

        if (string.IsNullOrWhiteSpace(profile.ProfileName))
        {
            result.Add(ValidationSeverity.Error, "Profile name is required.", nameof(profile.ProfileName));
        }

        if (profile.Mode == ProfileMode.Auto && string.IsNullOrWhiteSpace(profile.Target.Exe))
        {
            result.Add(ValidationSeverity.Warning, "Auto profiles need a target executable before they can activate.", "target.exe");
        }

        if (!string.IsNullOrWhiteSpace(profile.Target.Exe) &&
            Path.IsPathFullyQualified(profile.Target.Exe) &&
            _exeExists is not null &&
            !_exeExists(profile.Target.Exe))
        {
            result.Add(ValidationSeverity.Warning, "The target executable was not found.", "target.exe");
        }

        foreach (var duplicate in profile.Bindings
                     .Where(binding => !string.IsNullOrWhiteSpace(binding.Input))
                     .GroupBy(binding => KeyCatalog.Normalize(binding.Input), StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            result.Add(ValidationSeverity.Error, $"Multiple bindings use {KeyCatalog.LabelFor(duplicate.Key)}.", "bindings");
        }

        foreach (var binding in profile.Bindings)
        {
            ValidateBinding(binding, result);
        }

        return result;
    }

    public static void ReplaceBinding(KeyForgeProfile profile, KeyBinding binding)
    {
        var normalizedInput = KeyCatalog.Normalize(binding.Input);
        profile.Bindings.RemoveAll(existing =>
            string.Equals(KeyCatalog.Normalize(existing.Input), normalizedInput, StringComparison.OrdinalIgnoreCase));
        profile.Bindings.Add(binding);
        profile.ModifiedAt = DateTimeOffset.UtcNow;
    }

    private void ValidateBinding(KeyBinding binding, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(binding.Input))
        {
            result.Add(ValidationSeverity.Error, "Binding input key is required.", "bindings.input");
            return;
        }

        binding.Input = KeyCatalog.Normalize(binding.Input);
        if (!KeyCatalog.IsKnown(binding.Input))
        {
            result.Add(ValidationSeverity.Error, $"Unknown input key: {binding.Input}.", "bindings.input");
        }

        if (KeyCatalog.IsRisky(binding.Input))
        {
            result.Add(ValidationSeverity.Warning,
                $"Remapping {KeyCatalog.LabelFor(binding.Input)} can affect desktop or game shortcuts while the profile is active.",
                "bindings.input");
        }

        if (binding.Type == BindingType.Disabled)
        {
            return;
        }

        if (binding.Output.Count == 0)
        {
            result.Add(ValidationSeverity.Error, "Binding output is required.", "bindings.output");
            return;
        }

        if (binding.Type == BindingType.Macro)
        {
            if (binding.Output.Count > MaxMacroSteps)
            {
                result.Add(ValidationSeverity.Error, $"Macros are limited to {MaxMacroSteps} steps.", "bindings.output");
            }

            result.Add(ValidationSeverity.Warning,
                "Some online or competitive games prohibit macros. Check the game's rules before using this profile.",
                "bindings.output");
        }

        foreach (var step in binding.Output)
        {
            ValidateStep(step, result);
        }
    }

    private void ValidateStep(MacroStep step, ValidationResult result)
    {
        if (step.Action == MacroStepAction.Wait)
        {
            var delay = step.DelayMs ?? 0;
            if (delay < _settings.MacroMinimumDelayMs)
            {
                result.Add(ValidationSeverity.Error,
                    $"Wait steps must be at least {_settings.MacroMinimumDelayMs}ms.",
                    "bindings.output.delayMs");
            }

            if (delay > MaxMacroDelayMs)
            {
                result.Add(ValidationSeverity.Error,
                    $"Wait steps cannot exceed {MaxMacroDelayMs}ms.",
                    "bindings.output.delayMs");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(step.Key))
        {
            result.Add(ValidationSeverity.Error, "Keyboard macro steps require a key.", "bindings.output.key");
            return;
        }

        step.Key = KeyCatalog.Normalize(step.Key);
        if (!KeyCatalog.IsKnown(step.Key))
        {
            result.Add(ValidationSeverity.Error, $"Unknown output key: {step.Key}.", "bindings.output.key");
        }
    }
}
