namespace KeyForge.Core.Keyboard;

public sealed record KeyDefinition(string Code, string Label, double Width = 1.0, bool GapBefore = false);
