namespace KeyForge.Core.Validation;

public sealed class ValidationResult
{
    public List<ValidationIssue> Issues { get; } = [];

    public bool IsValid => Issues.All(issue => issue.Severity != ValidationSeverity.Error);

    public IEnumerable<ValidationIssue> Errors => Issues.Where(issue => issue.Severity == ValidationSeverity.Error);

    public IEnumerable<ValidationIssue> Warnings => Issues.Where(issue => issue.Severity == ValidationSeverity.Warning);

    public void Add(ValidationSeverity severity, string message, string? field = null)
    {
        Issues.Add(new ValidationIssue(severity, message, field));
    }
}
