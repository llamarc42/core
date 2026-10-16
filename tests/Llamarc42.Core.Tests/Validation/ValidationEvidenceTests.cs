using System.Text;
using System.Text.Json;
using Llamarc42.Core.Discovery;
using Llamarc42.Core.Tests.Discovery;
using Llamarc42.Core.Validation;

namespace Llamarc42.Core.Tests.Validation;

public sealed class ValidationEvidenceTests
{
    [Fact]
    public void EveryCodeUsesItsNormativePhase()
    {
        int[][] phases =
        [
            [1, 2, 3, 4, 5, 6, 7, 8, 9],
            [100, 101, 102, 103, 104, 109, 200, 201, 301],
            [150, 151, 202, 203, 204, 205, 206, 207, 208],
            [105, 106, 107],
            [210, 211, 212, 213, 214, 215, 216, 217, 218],
            [300, 302, 400],
        ];
        Assert.Equal(Enum.GetValues<ValidationDiagnosticCode>().Select(code => (int)code).Order(), phases.SelectMany(phase => phase).Order());
        for (var rank = 0; rank < phases.Length; rank++)
            foreach (var code in phases[rank]) Assert.Equal(rank + 1, ValidationDiagnosticCatalog.GetPhase((ValidationDiagnosticCode)code));

        var diagnostics = phases.SelectMany(phase => phase).Reverse()
            .Select(code => new ValidationDiagnostic((ValidationDiagnosticCode)code, "fixture", "same.md", "same.md", 1));
        var result = new DocumentationValidationResult(".", null, [], diagnostics);
        Assert.Equal(phases.SelectMany(phase => phase.Order()), result.Diagnostics.Select(diagnostic => (int)diagnostic.Code));
    }

    private static readonly string[] expected = ["guides/cafe\u0301.md", "guides/café.md"];

    [Fact]
    public void NfcCollisionsKeepDistinctSourcesAndLifecycleAuthority()
    {
        using var fixture = new DiscoveryFixture();
        var files = new Dictionary<string, (DocumentType Type, string Text, string? Id)>(StringComparer.Ordinal)
        {
            ["project.md"] = (DocumentType.ProjectDescriptor, "# Project\n\nDocumentation contract: llamarc42/v0.1\n\nPurpose.\n", null),
            ["history/café.md"] = (DocumentType.HistoricalRecord, "Status: Accepted", null),
            ["history/cafe\u0301.md"] = (DocumentType.HistoricalRecord, "history", null),
            ["README.md"] = (DocumentType.Navigation, "navigation", null),
            ["readme.md"] = (DocumentType.Supplemental, "supplement", null),
            ["guides/café.md"] = (DocumentType.CurrentGuidance, "guidance", null),
            ["guides/cafe\u0301.md"] = (DocumentType.CurrentGuidance, "guidance", null),
            ["specifications/café.md"] = (DocumentType.CurrentGuidance, "# Spec\n\nStatus: Accepted\n", null),
            ["specifications/cafe\u0301.md"] = (DocumentType.CurrentGuidance, "# Spec\n\nStatus: Proposed\n", null),
            ["decisions/ADR-001-one.md"] = (DocumentType.DecisionRecord, ContractCoverageTests.Adr("ADR-001", "Accepted"), "ADR-001"),
            ["decisions/ADR-001-two.md"] = (DocumentType.DecisionRecord, ContractCoverageTests.Adr("ADR-001", "Proposed"), "ADR-001"),
        };
        // Synthetic enumeration represents distinct locators even on normalization-insensitive filesystems.
        var discovery = new ProjectDiscoveryResult(fixture.Root, fixture.Root, ProjectDiscoveryOutcome.Discovered, fixture.Root,
            files.Select(file => new DocumentInventoryEntry(file.Key, file.Key.Normalize(), file.Value.Type, file.Value.Id)), [],
            files.Select(file => new DiscoveredEntry(file.Key, FileSystemEntryKind.RegularFile)));
        var result = DocumentationValidator.Validate(fixture.Root, _ => discovery,
            path => Encoding.UTF8.GetBytes(files[Path.GetRelativePath(fixture.Root, path).Replace(Path.DirectorySeparatorChar, '/')].Text));
        Assert.Null(result.ToolError);
        Assert.Equal(4, result.Diagnostics.Count(diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V104));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V201);
        Assert.DoesNotContain(result.Documents, document => document.AuthorityState == AuthorityState.Indeterminate);
        Assert.All(result.Documents.Where(document => document.DocumentType == DocumentType.HistoricalRecord), document => Assert.Equal(AuthorityState.NonCurrent, document.AuthorityState));
        Assert.All(result.Documents.Where(document => document.SourcePath.StartsWith("guides/", StringComparison.Ordinal)), document => Assert.Equal(AuthorityState.Current, document.AuthorityState));
        Assert.Equal(AuthorityState.Current, result.Documents.Single(document => document.SourcePath == "specifications/café.md").AuthorityState);
        Assert.Equal(AuthorityState.NonCurrent, result.Documents.Single(document => document.SourcePath == "specifications/cafe\u0301.md").AuthorityState);
        Assert.Equal(AuthorityState.Current, result.Documents.Single(document => document.SourcePath == "decisions/ADR-001-one.md").AuthorityState);
        Assert.Equal(AuthorityState.NonCurrent, result.Documents.Single(document => document.SourcePath == "decisions/ADR-001-two.md").AuthorityState);
        Assert.Equal(files.Count, result.Documents.Count);
        Assert.All(result.Diagnostics.Where(diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V104), diagnostic =>
        {
            Assert.Null(diagnostic.SourcePath);
            Assert.Equal(2, diagnostic.RelatedSourcePaths.Count);
        });
        var ordered = result.Documents.Where(document => document.Path == "guides/café.md").Select(document => document.SourcePath).ToArray();
        Assert.Equal(expected, ordered);
    }

    [Fact]
    public void MissingPrerequisitesSuppressDerivativeDiagnostics()
    {
        using var fixture = new DiscoveryFixture();
        fixture.Write("project.md", "# Project\n\nDocumentation Contract: wrong\nDocumentation contract: v2\n\nPurpose.\n");
        fixture.Write("specifications/spec.md", "# Spec\n\nStatus: Draft\nStatus: Other\n");
        fixture.Write("decisions/ADR-001-one.md", ContractCoverageTests.Adr("ADR-001", "Draft\nAccepted",
            "## Supersedes\n\n[missing](ADR-002-missing.md)\n"));
        fixture.Write("history/iterations/2026/٢٠٢٦-02-29-Bad.md", "history");
        var result = DocumentationValidator.Validate(fixture.Root);
        foreach (var code in new[] { ValidationDiagnosticCode.LC42V005, ValidationDiagnosticCode.LC42V006,
            ValidationDiagnosticCode.LC42V151, ValidationDiagnosticCode.LC42V205, ValidationDiagnosticCode.LC42V211,
            ValidationDiagnosticCode.LC42V215, ValidationDiagnosticCode.LC42V301, ValidationDiagnosticCode.LC42V302 })
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == code);
        foreach (var code in new[] { ValidationDiagnosticCode.LC42V004, ValidationDiagnosticCode.LC42V150,
            ValidationDiagnosticCode.LC42V204, ValidationDiagnosticCode.LC42V106, ValidationDiagnosticCode.LC42V109 })
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void RootAndFileStartsHaveEquivalentConformanceMeaning()
    {
        using var fixture = new DiscoveryFixture();
        foreach (var file in ContractCoverageTests.ValidFiles()) fixture.Write(file.Key, file.Value);
        var root = DocumentationValidator.Validate(fixture.Root);
        var fileResult = DocumentationValidator.Validate(Path.Combine(fixture.Root, "guides", "guide.md"));
        Assert.Equal(root.Outcome, fileResult.Outcome);
        Assert.Equal(JsonSerializer.Serialize(root.Documents), JsonSerializer.Serialize(fileResult.Documents));
        Assert.Equal(JsonSerializer.Serialize(root.Diagnostics), JsonSerializer.Serialize(fileResult.Diagnostics));
    }
}
