using KeyForge.Core.Keyboard;
using KeyForge.Core.Models;

namespace KeyForge.Input;

public sealed class RemappingEngine : IDisposable
{
    private readonly IKeyboardHook _keyboardHook;
    private readonly IInputSender _inputSender;
    private readonly IMacroRunner _macroRunner;
    private readonly AppSettings _settings;
    private readonly object _gate = new();
    private readonly HashSet<string> _physicalKeysDown = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _heldOutputKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _macroRuns = new(StringComparer.OrdinalIgnoreCase);
    private List<KeyForgeProfile> _activeProfiles = [];
    private bool _disposed;

    public RemappingEngine(
        IKeyboardHook keyboardHook,
        IInputSender inputSender,
        IMacroRunner macroRunner,
        AppSettings settings)
    {
        _keyboardHook = keyboardHook;
        _inputSender = inputSender;
        _macroRunner = macroRunner;
        _settings = settings;
        _keyboardHook.KeyEvent += OnKeyboardEvent;
    }

    public event EventHandler<string>? StatusChanged;

    public KeyForgeProfile? ActiveProfile
    {
        get
        {
            lock (_gate)
            {
                return _activeProfiles.FirstOrDefault();
            }
        }
    }

    public IReadOnlyList<KeyForgeProfile> ActiveProfiles
    {
        get
        {
            lock (_gate)
            {
                return _activeProfiles.ToList();
            }
        }
    }

    public bool IsPaused { get; private set; }

    public void Start() => _keyboardHook.Start();

    public void Stop()
    {
        CancelMacros();
        _inputSender.ReleaseAll();
        _keyboardHook.Stop();
    }

    public void SetActiveProfile(KeyForgeProfile? profile)
    {
        SetActiveProfiles(profile is null ? [] : [profile]);
    }

    public void SetActiveProfiles(IReadOnlyList<KeyForgeProfile> profiles)
    {
        lock (_gate)
        {
            if (SequencesMatch(_activeProfiles, profiles))
            {
                return;
            }

            CancelMacros();
            _heldOutputKeys.Clear();
            _inputSender.ReleaseAll();
            _activeProfiles = profiles.ToList();
        }

        StatusChanged?.Invoke(this, DescribeActiveProfiles(profiles));
    }

    public void SetPaused(bool paused)
    {
        IsPaused = paused;
        if (paused)
        {
            CancelMacros();
            _heldOutputKeys.Clear();
            _inputSender.ReleaseAll();
        }

        StatusChanged?.Invoke(this, paused ? "Remapping paused" : "Remapping running");
    }

    public void EmergencyDisable()
    {
        SetPaused(true);
        StatusChanged?.Invoke(this, "Emergency disable activated");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _keyboardHook.KeyEvent -= OnKeyboardEvent;
        CancelMacros();
        _inputSender.ReleaseAll();
        _keyboardHook.Dispose();
        _disposed = true;
    }

    private void OnKeyboardEvent(object? sender, KeyboardHookEventArgs e)
    {
        try
        {
            var key = KeyCatalog.Normalize(e.Key);
            UpdatePhysicalState(key, e.IsKeyDown);

            if (e.IsKeyDown && IsEmergencyHotkeyDown())
            {
                e.Suppress = true;
                EmergencyDisable();
                return;
            }

            if (_settings.IgnoreInjectedInput && e.IsInjected)
            {
                return;
            }

            List<KeyForgeProfile> profiles;
            lock (_gate)
            {
                profiles = _activeProfiles.ToList();
            }

            if (IsPaused || profiles.Count == 0)
            {
                return;
            }

            var binding = FindFirstBinding(profiles, key);

            if (binding is null)
            {
                return;
            }

            e.Suppress = binding.BlockOriginal || binding.Type == BindingType.Disabled;
            ApplyBinding(binding, e.IsKeyDown);
        }
        catch (Exception ex)
        {
            e.Suppress = false;
            CancelMacros();
            _heldOutputKeys.Clear();
            _inputSender.ReleaseAll();
            IsPaused = true;
            StatusChanged?.Invoke(this, $"Remapping paused after input error: {ex.Message}");
        }
    }

    private void ApplyBinding(KeyBinding binding, bool isKeyDown)
    {
        switch (binding.Type)
        {
            case BindingType.Disabled:
                return;
            case BindingType.Simple:
                ApplySimpleBinding(binding, isKeyDown);
                return;
            case BindingType.Combo:
                if (isKeyDown && !_heldOutputKeys.ContainsKey(binding.Input))
                {
                    _heldOutputKeys[binding.Input] = [];
                    _ = RunMacroOnceAsync(binding.Input, binding.Output);
                }
                else if (!isKeyDown)
                {
                    _heldOutputKeys.Remove(binding.Input);
                }

                return;
            case BindingType.Macro:
                ApplyMacroBinding(binding, isKeyDown);
                return;
        }
    }

    private void ApplySimpleBinding(KeyBinding binding, bool isKeyDown)
    {
        var outputKey = binding.Output.FirstOrDefault(step => step.Action == MacroStepAction.Press)?.Key;
        if (string.IsNullOrWhiteSpace(outputKey))
        {
            return;
        }

        if (isKeyDown)
        {
            if (_heldOutputKeys.ContainsKey(binding.Input))
            {
                return;
            }

            _inputSender.KeyDown(outputKey);
            _heldOutputKeys[binding.Input] = [outputKey];
        }
        else if (_heldOutputKeys.Remove(binding.Input, out var heldKeys))
        {
            foreach (var held in heldKeys.AsEnumerable().Reverse())
            {
                _inputSender.KeyUp(held);
            }
        }
    }

    private void ApplyMacroBinding(KeyBinding binding, bool isKeyDown)
    {
        if (isKeyDown)
        {
            if (_macroRuns.ContainsKey(binding.Input))
            {
                return;
            }

            var cts = new CancellationTokenSource();
            _macroRuns[binding.Input] = cts;
            _ = Task.Run(async () =>
            {
                try
                {
                    do
                    {
                        await _macroRunner.RunAsync(binding.Output, cts.Token);
                    }
                    while (binding.RepeatWhileHeld && !cts.IsCancellationRequested && IsPhysicalKeyDown(binding.Input));
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    lock (_gate)
                    {
                        _macroRuns.Remove(binding.Input);
                    }
                }
            });
        }
        else if (binding.RepeatWhileHeld && _macroRuns.TryGetValue(binding.Input, out var cts))
        {
            cts.Cancel();
        }
    }

    private async Task RunMacroOnceAsync(string input, IReadOnlyList<MacroStep> output)
    {
        try
        {
            await _macroRunner.RunAsync(output);
        }
        catch
        {
            _inputSender.ReleaseAll();
            throw;
        }
        finally
        {
            _heldOutputKeys.Remove(input);
        }
    }

    private void UpdatePhysicalState(string key, bool isDown)
    {
        lock (_gate)
        {
            if (isDown)
            {
                _physicalKeysDown.Add(key);
            }
            else
            {
                _physicalKeysDown.Remove(key);
            }
        }
    }

    private bool IsPhysicalKeyDown(string key)
    {
        lock (_gate)
        {
            return _physicalKeysDown.Contains(KeyCatalog.Normalize(key));
        }
    }

    private bool IsEmergencyHotkeyDown()
    {
        lock (_gate)
        {
            var ctrl = _physicalKeysDown.Contains("LeftCtrl") || _physicalKeysDown.Contains("RightCtrl");
            var shift = _physicalKeysDown.Contains("LeftShift") || _physicalKeysDown.Contains("RightShift");
            return ctrl && shift && _physicalKeysDown.Contains("F12");
        }
    }

    private void CancelMacros()
    {
        foreach (var cts in _macroRuns.Values.ToList())
        {
            cts.Cancel();
            cts.Dispose();
        }

        _macroRuns.Clear();
    }

    private static KeyBinding? FindFirstBinding(IEnumerable<KeyForgeProfile> profiles, string key)
    {
        foreach (var profile in profiles)
        {
            if (!profile.Enabled || profile.Mode == ProfileMode.Off)
            {
                continue;
            }

            var binding = profile.Bindings.FirstOrDefault(candidate =>
                string.Equals(KeyCatalog.Normalize(candidate.Input), key, StringComparison.OrdinalIgnoreCase));
            if (binding is not null)
            {
                return binding;
            }
        }

        return null;
    }

    private static bool SequencesMatch(IReadOnlyList<KeyForgeProfile> current, IReadOnlyList<KeyForgeProfile> next)
    {
        if (current.Count != next.Count)
        {
            return false;
        }

        for (var index = 0; index < current.Count; index++)
        {
            if (!string.Equals(current[index].ProfileId, next[index].ProfileId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string DescribeActiveProfiles(IReadOnlyList<KeyForgeProfile> profiles)
    {
        return profiles.Count switch
        {
            0 => "No active profile",
            1 => $"Active profile: {profiles[0].ProfileName}",
            _ => $"Active profile stack: {profiles[0].ProfileName} + {profiles.Count - 1} overlay{(profiles.Count == 2 ? string.Empty : "s")}"
        };
    }
}
