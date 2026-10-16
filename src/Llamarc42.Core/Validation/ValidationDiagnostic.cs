using System.Text;
using Llamarc42.Core.Discovery;

namespace Llamarc42.Core.Validation;

/// <summary>A deterministic conformance diagnostic.</summary>
public sealed class ValidationDiagnostic
{
    internal ValidationDiagnostic(
        ValidationDiagnosticCode code,
        string message,
        string? path = null,
        string? sourcePath = null,
        int? line = null,
        IEnumerable<string>? relatedSourcePaths = null,
        string? destination = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("A validation diagnostic message cannot be empty.", nameof(message));
        }

        if (line is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(line), "A source line must be one-based.");
        }

        Code = code;
        Severity = ValidationDiagnosticCatalog.GetSeverity(code);
        Message = message;
        Path = path?.Normalize(NormalizationForm.FormC);
        SourcePath = sourcePath;
        Line = line;
        RelatedSourcePaths = Array.AsReadOnly(
            (relatedSourcePaths ?? [])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, UnicodeCodePointComparer.Instance)
                .ToArray());
        OriginalDestination = destination;
        NormalizedDestination = destination?.Normalize(NormalizationForm.FormC);
    }

    /// <summary>The stable contract code.</summary>
    public ValidationDiagnosticCode Code { get; }

    /// <summary>The severity fixed by the contract code.</summary>
    public ValidationSeverity Severity { get; }

    /// <summary>Deterministic, actionable text.</summary>
    public string Message { get; }

    /// <summary>NFC-normalized project-relative path, when the condition belongs to a document.</summary>
    public string? Path { get; }

    /// <summary>Original source locator, when the condition belongs to one source entry.</summary>
    public string? SourcePath { get; }

    /// <summary>One-based source line, when required by the diagnostic.</summary>
    public int? Line { get; }

    /// <summary>Ordinally ordered original source locators related to a set diagnostic.</summary>
    public IReadOnlyList<string> RelatedSourcePaths { get; }

    internal string? NormalizedDestination { get; }

    internal string? OriginalDestination { get; }
}
