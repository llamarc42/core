using Llamarc42.Core.Discovery;
using Llamarc42.Core.Tests.Discovery;
using Llamarc42.Core.Validation;

namespace Llamarc42.Core.Tests.Validation;

public sealed class ValidationCatalogFixtureTests
{
    [Fact]
    public void DiscoveryOutcomesMapToRootDiagnosticsAndToolErrors()
    {
        using var missing = new DiscoveryFixture();
        var noRoot = DocumentationValidator.Validate(missing.Root);
        var noStart = DocumentationValidator.Validate(Path.Combine(missing.Root, "absent"));

        using var ancestor = new DiscoveryFixture();
        ancestor.Write("project.md", ValidProject);
        ancestor.CreateDirectory("child/project.md");
        var invalidMarker = DocumentationValidator.Validate(ancestor.CreateDirectory("child/deeper"));

        AssertCode(noRoot, ValidationDiagnosticCode.LC42V001);
        Assert.Equal(ValidationToolErrorCode.LC42T001, noStart.ToolError?.Code);
        AssertCode(invalidMarker, ValidationDiagnosticCode.LC42V002);
    }

    [Fact]
    public void PortablePathRulesUseSyntheticSourceLocatorsForHostIndependentCoverage()
    {
        var diagnostics = DocumentationValidator.ValidatePortablePathsForTesting(
            Entry("bad?/one.md"), Entry("bad./two.md"), Entry("CON.md"),
            Entry("Straße.md"), Entry("STRASSE.md"));

        Assert.Contains(diagnostics, item => item.Code == ValidationDiagnosticCode.LC42V101);
        Assert.Contains(diagnostics, item => item.Code == ValidationDiagnosticCode.LC42V102);
        Assert.Contains(diagnostics, item => item.Code == ValidationDiagnosticCode.LC42V103);
        Assert.Contains(diagnostics, item => item.Code == ValidationDiagnosticCode.LC42V104);
    }

    [Fact]
    public void SpecificationDuplicateStatusEmitsV150()
    {
        using var fixture = Project();
        fixture.Write("specifications/test.md", "# Spec\n\nStatus: Accepted\nStatus: Proposed\n\n## Scope\n\nText.\n");

        var result = DocumentationValidator.Validate(fixture.Root);

        AssertCode(result, ValidationDiagnosticCode.LC42V150);
        Assert.Equal(AuthorityState.Indeterminate, result.Documents.Single(document => document.SourcePath == "specifications/test.md").AuthorityState);
    }

    [Fact]
    public void AdrCandidateIdentityAndFormCodesAreEmitted()
    {
        using var fixture = Project();
        fixture.Write("decisions/ADR-7-Bad.md", "bad");
        fixture.Write("decisions/ADR-001-one.md", "# Wrong\n\n## Status\n\nDraft\n");
        fixture.Write("decisions/ADR-001-two.md", "# ADR-001\n\n## Status\n\nAccepted\n");

        var result = DocumentationValidator.Validate(fixture.Root);

        foreach (var code in new[]
        {
            ValidationDiagnosticCode.LC42V200, ValidationDiagnosticCode.LC42V201,
            ValidationDiagnosticCode.LC42V202, ValidationDiagnosticCode.LC42V205,
            ValidationDiagnosticCode.LC42V206, ValidationDiagnosticCode.LC42V207,
            ValidationDiagnosticCode.LC42V208,
        }) AssertCode(result, code);
    }

    [Fact]
    public void AdrMissingAndAmbiguousStatusCodesAreEmitted()
    {
        using var missing = Project();
        missing.Write("decisions/ADR-001-one.md", Adr("ADR-001", statusSection: string.Empty));
        using var duplicate = Project();
        duplicate.Write("decisions/ADR-001-one.md", Adr("ADR-001", "## Status\n\nAccepted\nMore"));

        AssertCode(DocumentationValidator.Validate(missing.Root), ValidationDiagnosticCode.LC42V203);
        AssertCode(DocumentationValidator.Validate(duplicate.Root), ValidationDiagnosticCode.LC42V204);
    }

    [Fact]
    public void RelationshipStructuralCodesAreEmitted()
    {
        using var fixture = Project();
        fixture.Write("decisions/ADR-001-one.md", Adr("ADR-001", "## Status\n\nAccepted",
            "## Supersedes\n\n[self](ADR-001-one.md)\n[self again](ADR-001-one.md)\n\n## Supersedes\n\n[external](https://example.com/x)"));

        var result = DocumentationValidator.Validate(fixture.Root);

        foreach (var code in new[]
        {
            ValidationDiagnosticCode.LC42V210, ValidationDiagnosticCode.LC42V211,
            ValidationDiagnosticCode.LC42V212, ValidationDiagnosticCode.LC42V213,
        }) AssertCode(result, code);
    }

    [Fact]
    public void SupersessionCycleAndAmendmentRulesAreEmitted()
    {
        using var fixture = Project();
        fixture.Write("decisions/ADR-001-one.md", Adr("ADR-001", "## Status\n\nSuperseded",
            "## Superseded by\n\n[two](ADR-002-two.md)"));
        fixture.Write("decisions/ADR-002-two.md", Adr("ADR-002", "## Status\n\nSuperseded",
            "## Superseded by\n\n[one](ADR-001-one.md)"));
        fixture.Write("decisions/ADR-003-three.md", Adr("ADR-003", "## Status\n\nAccepted",
            "## Amends\n\n[four](ADR-004-four.md)"));
        fixture.Write("decisions/ADR-004-four.md", Adr("ADR-004", "## Status\n\nProposed"));

        var result = DocumentationValidator.Validate(fixture.Root);

        AssertCode(result, ValidationDiagnosticCode.LC42V216);
        AssertCode(result, ValidationDiagnosticCode.LC42V217);
        AssertCode(result, ValidationDiagnosticCode.LC42V218);
    }

    [Fact]
    public void IterationPlacementWarningIsNonInvalidatingWhenAlone()
    {
        using var fixture = Project();
        fixture.Write("history/iterations/design-notes.md", "Notes.\n");

        var result = DocumentationValidator.Validate(fixture.Root);

        AssertCode(result, ValidationDiagnosticCode.LC42V300);
        Assert.Equal(ValidationSeverity.Warning, result.Diagnostics.Single(item => item.Code == ValidationDiagnosticCode.LC42V300).Severity);
        Assert.Equal(ValidationOutcome.Valid, result.Outcome);
    }

    private static DiscoveryFixture Project()
    {
        var fixture = new DiscoveryFixture();
        fixture.Write("project.md", ValidProject);
        return fixture;
    }

    private static DocumentInventoryEntry Entry(string path) => new(path, path.Normalize(), DocumentType.Supplemental, null);

    private static void AssertCode(DocumentationValidationResult result, ValidationDiagnosticCode code) =>
        Assert.Contains(result.Diagnostics, item => item.Code == code);

    private static string Adr(string id, string statusSection, string relationships = "") => $"""
        # {id}: Example

        {statusSection}

        ## Context

        Context.

        ## Decision

        Decision.

        ## Consequences

        Consequences.
        """ + "\n\n" + relationships + "\n";

    private const string ValidProject = "# Example\n\nDocumentation contract: llamarc42/v0.1\n\nPurpose.\n";
}
