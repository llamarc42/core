using Llamarc42.Core.Discovery;
using Llamarc42.Core.Validation;

namespace Llamarc42.Core.Tests.Validation;

public sealed class ValidationResultTests
{
    [Fact]
    public void CatalogAssignsEveryContractCodeToExactlyOnePhaseAndFixedSeverity()
    {
        var codes = Enum.GetValues<ValidationDiagnosticCode>();

        Assert.Equal(42, codes.Length);
        Assert.All(codes, code => Assert.InRange(ValidationDiagnosticCatalog.GetPhase(code), 1, 6));
        Assert.Equal(
            [ValidationDiagnosticCode.LC42V300, ValidationDiagnosticCode.LC42V302, ValidationDiagnosticCode.LC42V400],
            codes.Where(code => ValidationDiagnosticCatalog.GetSeverity(code) == ValidationSeverity.Warning));
    }

    [Fact]
    public void ResultDerivesAggregateOutcomeAndToolErrorExclusivity()
    {
        var warning = Diagnostic(ValidationDiagnosticCode.LC42V300);
        var error = Diagnostic(ValidationDiagnosticCode.LC42V003);

        var valid = Result([warning]);
        var invalid = Result([warning, error]);
        var failed = Result(
            [error],
            new ValidationToolError(ValidationToolErrorCode.LC42T003, "Reading project.md failed; correct access and retry."));

        Assert.Equal(ValidationOutcome.Valid, valid.Outcome);
        Assert.Null(valid.ToolError);
        Assert.Equal(ValidationOutcome.Invalid, invalid.Outcome);
        Assert.Null(invalid.ToolError);
        Assert.Equal(ValidationOutcome.Error, failed.Outcome);
        Assert.Equal(ValidationToolErrorCode.LC42T003, failed.ToolError?.Code);
        Assert.Equal(DocumentationValidationResult.CurrentContractVersion, failed.ContractVersion);
    }

    [Fact]
    public void ResultOrdersDiagnosticsByEveryContractKey()
    {
        var destinationLater = Diagnostic(ValidationDiagnosticCode.LC42V105, path: "b.md", destination: "z.md");
        var destinationEarlier = Diagnostic(ValidationDiagnosticCode.LC42V105, path: "b.md", destination: "a.md");
        var relatedLater = Diagnostic(ValidationDiagnosticCode.LC42V104, path: "b.md", related: ["z.md"]);
        var relatedEarlier = Diagnostic(ValidationDiagnosticCode.LC42V104, path: "b.md", related: ["a.md"]);
        var codeLater = Diagnostic(ValidationDiagnosticCode.LC42V103, path: "b.md", sourcePath: "b.md", line: 2);
        var codeEarlier = Diagnostic(ValidationDiagnosticCode.LC42V102, path: "b.md", sourcePath: "b.md", line: 2);
        var noLine = Diagnostic(ValidationDiagnosticCode.LC42V102, path: "b.md", sourcePath: "b.md");
        var presentLine = Diagnostic(ValidationDiagnosticCode.LC42V102, path: "b.md", sourcePath: "b.md", line: 1);
        var sourceLater = Diagnostic(ValidationDiagnosticCode.LC42V102, path: "b.md", sourcePath: "z.md");
        var sourceEarlier = Diagnostic(ValidationDiagnosticCode.LC42V102, path: "b.md", sourcePath: "a.md");
        var pathLater = Diagnostic(ValidationDiagnosticCode.LC42V102, path: "z.md");
        var pathEarlier = Diagnostic(ValidationDiagnosticCode.LC42V102, path: "a.md");
        var phaseLater = Diagnostic(ValidationDiagnosticCode.LC42V105);
        var phaseEarlier = Diagnostic(ValidationDiagnosticCode.LC42V003);

        AssertOrdered(phaseEarlier, phaseLater);
        AssertOrdered(pathEarlier, pathLater);
        AssertOrdered(sourceEarlier, sourceLater);
        AssertOrdered(presentLine, noLine);
        AssertOrdered(codeEarlier, codeLater);
        AssertOrdered(relatedEarlier, relatedLater);
        AssertOrdered(destinationEarlier, destinationLater);
    }

    [Fact]
    public void DiagnosticPreservesSourceEvidenceAndNormalizesOnlyStableIdentity()
    {
        var diagnostic = new ValidationDiagnostic(
            ValidationDiagnosticCode.LC42V104,
            "Collision detected.",
            path: "cafe\u0301.md",
            relatedSourcePaths: ["z.md", "cafe\u0301.md", "café.md", "z.md"]);

        Assert.Equal("café.md", diagnostic.Path);
        Assert.Equal(["cafe\u0301.md", "café.md", "z.md"], diagnostic.RelatedSourcePaths);
    }

    [Fact]
    public void ResultSnapshotsAndOrdersDocumentsByContractGrouping()
    {
        var source = new List<ValidationDocument>
        {
            Document("notes.md", DocumentType.Supplemental),
            Document("decisions/ADR-010-later.md", DocumentType.DecisionRecord, "ADR-010"),
            Document("project.md", DocumentType.ProjectDescriptor),
            Document("decisions/ADR-002-earlier.md", DocumentType.DecisionRecord, "ADR-002"),
        };

        var result = new DocumentationValidationResult(".", "C:/project", source, []);
        source.Clear();

        Assert.Equal(
            ["project.md", "decisions/ADR-002-earlier.md", "decisions/ADR-010-later.md", "notes.md"],
            result.Documents.Select(document => document.SourcePath));
    }

    private static DocumentationValidationResult Result(
        IEnumerable<ValidationDiagnostic> diagnostics,
        ValidationToolError? toolError = null) =>
        new("supplied", projectRoot: null, documents: [], diagnostics, toolError);

    private static ValidationDiagnostic Diagnostic(
        ValidationDiagnosticCode code,
        string? path = null,
        string? sourcePath = null,
        int? line = null,
        IEnumerable<string>? related = null,
        string? destination = null) =>
        new(code, $"Diagnostic {code}.", path, sourcePath, line, related, destination);

    private static ValidationDocument Document(
        string path,
        DocumentType type,
        string? decisionId = null) =>
        new(path, path, type, AuthorityState.None, LifecycleStatus: null, decisionId);

    private static void AssertOrdered(ValidationDiagnostic expectedFirst, ValidationDiagnostic expectedSecond)
    {
        var result = Result([expectedSecond, expectedFirst]);
        Assert.Same(expectedFirst, result.Diagnostics[0]);
        Assert.Same(expectedSecond, result.Diagnostics[1]);
    }
}
