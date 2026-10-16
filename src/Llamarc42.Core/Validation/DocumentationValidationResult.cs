using System.Globalization;
using Llamarc42.Core.Discovery;

namespace Llamarc42.Core.Validation;

/// <summary>The immutable structured result of deterministic contract validation.</summary>
public sealed class DocumentationValidationResult
{
    /// <summary>The validation contract implemented by this result model.</summary>
    public const string CurrentContractVersion = "llamarc42/v0.1";

    internal DocumentationValidationResult(
        string suppliedPath,
        string? projectRoot,
        IEnumerable<ValidationDocument> documents,
        IEnumerable<ValidationDiagnostic> diagnostics,
        ValidationToolError? toolError = null)
    {
        ContractVersion = CurrentContractVersion;
        SuppliedPath = suppliedPath;
        ProjectRoot = projectRoot;
        Documents = Array.AsReadOnly(
            documents.OrderBy(document => document, ValidationDocumentComparer.Instance).ToArray());
        Diagnostics = Array.AsReadOnly(
            diagnostics.OrderBy(diagnostic => diagnostic, ValidationDiagnosticComparer.Instance).ToArray());
        ToolError = toolError;
        Outcome = toolError is not null
            ? ValidationOutcome.Error
            : Diagnostics.Any(diagnostic => diagnostic.Severity == ValidationSeverity.Error)
                ? ValidationOutcome.Invalid
                : ValidationOutcome.Valid;
    }

    /// <summary>The exact selected validation contract version.</summary>
    public string ContractVersion { get; }

    /// <summary>The caller-supplied starting path, preserved without normalization.</summary>
    public string SuppliedPath { get; }

    /// <summary>The normalized absolute project root when discovery established one.</summary>
    public string? ProjectRoot { get; }

    /// <summary>The aggregate validation outcome.</summary>
    public ValidationOutcome Outcome { get; }

    /// <summary>The complete ordered inventory available before any tool failure.</summary>
    public IReadOnlyList<ValidationDocument> Documents { get; }

    /// <summary>The complete contract-ordered diagnostics available before any tool failure.</summary>
    public IReadOnlyList<ValidationDiagnostic> Diagnostics { get; }

    /// <summary>The failure that prevented complete evaluation; present only for <see cref="ValidationOutcome.Error"/>.</summary>
    public ValidationToolError? ToolError { get; }

    private sealed class ValidationDocumentComparer : IComparer<ValidationDocument>
    {
        internal static ValidationDocumentComparer Instance { get; } = new();

        public int Compare(ValidationDocument? x, ValidationDocument? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var comparison = x.DocumentType.CompareTo(y.DocumentType);
            if (comparison == 0 && x.DocumentType == DocumentType.DecisionRecord)
            {
                comparison = ParseDecisionNumber(x.DecisionId).CompareTo(ParseDecisionNumber(y.DecisionId));
            }

            comparison = comparison != 0
                ? comparison
                : UnicodeCodePointComparer.Instance.Compare(x.Path, y.Path);
            return comparison != 0
                ? comparison
                : UnicodeCodePointComparer.Instance.Compare(x.SourcePath, y.SourcePath);
        }

        private static int ParseDecisionNumber(string? decisionId) =>
            decisionId is not null &&
            decisionId.Length == 7 &&
            int.TryParse(decisionId.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                ? number
                : int.MaxValue;
    }
}
