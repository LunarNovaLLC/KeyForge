namespace KeyForge.Core.Keyboard;

public sealed record KeyVisualDefinition(
    string Code,
    string Label,
    double X,
    double Y,
    double Width = 1.0,
    double Height = 1.0);
