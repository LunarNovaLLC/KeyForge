namespace KeyForge.Core.Models;

public sealed class KeyForgeProfile
{
    public string Version { get; set; } = "0.1.0";

    public string ProfileId { get; set; } = Guid.NewGuid().ToString("n");

    public string ProfileName { get; set; } = "New Profile";

    public ProfileTarget Target { get; set; } = new();

    public string? ArtworkPath { get; set; }

    public string? IconPath { get; set; }

    public string? AccentHint { get; set; }

    public ProfileMode Mode { get; set; } = ProfileMode.Auto;

    public bool Enabled { get; set; } = true;

    public List<KeyBinding> Bindings { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;

    public string AppVersion { get; set; } = "0.1.0";

    public override string ToString() => ProfileName;
}
