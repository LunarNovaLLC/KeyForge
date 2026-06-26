namespace KeyForge.Core.Models;

public sealed class ProfileTarget
{
    public string Exe { get; set; } = string.Empty;

    public string? ExecutablePath { get; set; }

    public string? WindowTitleContains { get; set; }
}
