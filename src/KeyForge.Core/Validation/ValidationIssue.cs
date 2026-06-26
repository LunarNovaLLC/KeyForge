namespace KeyForge.Core.Validation;

public sealed record ValidationIssue(ValidationSeverity Severity, string Message, string? Field = null);
