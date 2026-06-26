namespace KeyForge.Core.Models;

public sealed class ActiveWindowInfo
{
    public static readonly ActiveWindowInfo Empty = new();

    public int ProcessId { get; set; }

    public string ProcessName { get; set; } = string.Empty;

    public string ExecutableName { get; set; } = string.Empty;

    public string? ExecutablePath { get; set; }

    public string WindowTitle { get; set; } = string.Empty;

    public bool? IsElevated { get; set; }
}
