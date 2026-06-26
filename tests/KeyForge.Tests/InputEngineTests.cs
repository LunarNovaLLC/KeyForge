using KeyForge.Core.Models;
using KeyForge.Input;
using System.Runtime.InteropServices;

namespace KeyForge.Tests;

public sealed class InputEngineTests
{
    [Fact]
    public void Win32InputStructHasNativeSize()
    {
        var expectedSize = Environment.Is64BitProcess ? 40 : 28;

        Assert.Equal(expectedSize, Marshal.SizeOf<Win32KeyboardNative.Input>());
    }

    [Fact]
    public async Task MacroRunnerEmitsStepsInOrder()
    {
        var input = new FakeInputSender();
        var runner = new MacroRunner(input, new AppSettings { MacroMinimumDelayMs = 20 });

        await runner.RunAsync(
        [
            MacroStep.KeyDown("LeftCtrl"),
            MacroStep.Press("B"),
            MacroStep.Wait(20),
            MacroStep.KeyUp("LeftCtrl")
        ]);

        Assert.Equal(
        [
            "down:LeftCtrl",
            "down:B",
            "up:B",
            "up:LeftCtrl"
        ], input.Log);
    }

    [Fact]
    public void RemappingEngineIgnoresInjectedInput()
    {
        var hook = new FakeKeyboardHook();
        var input = new FakeInputSender();
        var engine = CreateEngine(hook, input, new AppSettings { IgnoreInjectedInput = true });
        engine.SetActiveProfile(CreateSimpleProfile());
        engine.Start();

        var args = hook.Raise("LeftAlt", isDown: true, isInjected: true);

        Assert.False(args.Suppress);
        Assert.Empty(input.Log);
    }

    [Fact]
    public void RemappingEngineSuppressesAndHoldsSimpleRemap()
    {
        var hook = new FakeKeyboardHook();
        var input = new FakeInputSender();
        var engine = CreateEngine(hook, input, new AppSettings { IgnoreInjectedInput = true });
        engine.SetActiveProfile(CreateSimpleProfile());
        engine.Start();

        var down = hook.Raise("LeftAlt", isDown: true, isInjected: false);
        var up = hook.Raise("LeftAlt", isDown: false, isInjected: false);

        Assert.True(down.Suppress);
        Assert.True(up.Suppress);
        Assert.Equal(["down:B", "up:B"], input.Log);
    }

    [Fact]
    public void RemappingEngineUsesFirstActiveProfileBindingForConflicts()
    {
        var hook = new FakeKeyboardHook();
        var input = new FakeInputSender();
        var engine = CreateEngine(hook, input, new AppSettings { IgnoreInjectedInput = true });
        var foreground = CreateSimpleProfile();
        foreground.ProfileName = "Foreground";
        var overlay = new KeyForgeProfile
        {
            ProfileName = "Overlay",
            Mode = ProfileMode.AlwaysOn,
            Bindings =
            [
                new KeyBinding
                {
                    Input = "LeftAlt",
                    Type = BindingType.Simple,
                    BlockOriginal = true,
                    Output = [MacroStep.Press("C")]
                },
                new KeyBinding
                {
                    Input = "F1",
                    Type = BindingType.Simple,
                    BlockOriginal = true,
                    Output = [MacroStep.Press("D")]
                }
            ]
        };
        engine.SetActiveProfiles([foreground, overlay]);
        engine.Start();

        hook.Raise("LeftAlt", isDown: true, isInjected: false);
        hook.Raise("LeftAlt", isDown: false, isInjected: false);
        hook.Raise("F1", isDown: true, isInjected: false);
        hook.Raise("F1", isDown: false, isInjected: false);

        Assert.Equal(["down:B", "up:B", "down:D", "up:D"], input.Log);
    }

    [Fact]
    public void EmergencyDisablePausesEngineAndReleasesKeys()
    {
        var hook = new FakeKeyboardHook();
        var input = new FakeInputSender();
        var engine = CreateEngine(hook, input, new AppSettings());
        engine.SetActiveProfile(CreateSimpleProfile());
        engine.Start();

        hook.Raise("LeftAlt", isDown: true, isInjected: false);
        hook.Raise("LeftCtrl", isDown: true, isInjected: false);
        hook.Raise("LeftShift", isDown: true, isInjected: false);
        var f12 = hook.Raise("F12", isDown: true, isInjected: false);

        Assert.True(f12.Suppress);
        Assert.True(engine.IsPaused);
        Assert.Contains("up:B", input.Log);
    }

    private static RemappingEngine CreateEngine(FakeKeyboardHook hook, FakeInputSender input, AppSettings settings)
    {
        return new RemappingEngine(hook, input, new MacroRunner(input, settings), settings);
    }

    private static KeyForgeProfile CreateSimpleProfile()
    {
        return new KeyForgeProfile
        {
            ProfileName = "Notepad",
            Target = new ProfileTarget { Exe = "notepad.exe" },
            Bindings =
            [
                new KeyBinding
                {
                    Input = "LeftAlt",
                    Type = BindingType.Simple,
                    BlockOriginal = true,
                    Output = [MacroStep.Press("B")]
                }
            ]
        };
    }

    private sealed class FakeKeyboardHook : IKeyboardHook
    {
        public event EventHandler<KeyboardHookEventArgs>? KeyEvent;

        public bool IsRunning { get; private set; }

        public void Start() => IsRunning = true;

        public void Stop() => IsRunning = false;

        public KeyboardHookEventArgs Raise(string key, bool isDown, bool isInjected)
        {
            var args = new KeyboardHookEventArgs(key, isDown, isInjected, 0);
            KeyEvent?.Invoke(this, args);
            return args;
        }

        public void Dispose()
        {
            IsRunning = false;
        }
    }

    private sealed class FakeInputSender : IInputSender
    {
        private readonly HashSet<string> _pressed = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Log { get; } = [];

        public IReadOnlyCollection<string> PressedKeys => _pressed.ToList().AsReadOnly();

        public void KeyDown(string key)
        {
            _pressed.Add(key);
            Log.Add($"down:{key}");
        }

        public void KeyUp(string key)
        {
            _pressed.Remove(key);
            Log.Add($"up:{key}");
        }

        public async Task PressAsync(string key, int holdMs = 12, CancellationToken cancellationToken = default)
        {
            KeyDown(key);
            await Task.Delay(1, cancellationToken);
            KeyUp(key);
        }

        public void ReleaseAll()
        {
            foreach (var key in _pressed.Reverse().ToList())
            {
                KeyUp(key);
            }
        }
    }
}
