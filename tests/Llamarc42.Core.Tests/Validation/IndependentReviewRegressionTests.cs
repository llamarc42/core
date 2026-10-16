using Llamarc42.Core.Tests.Discovery;
using Llamarc42.Core.Validation;

namespace Llamarc42.Core.Tests.Validation;

// Phase 2 implementation regressions, not independent VAL-01 conformance evidence.
public sealed class IndependentReviewRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalDotSegmentsCannotSupplyReciprocityOrSuccessor(bool externalSuccessor)
    {
        using var fixture = Project();
        var oldTarget = "ADR-001-old.md";
        var newTarget = "ADR-002-new.md";
        WritePair(fixture, oldTarget, newTarget);
        Assert.Equal(ValidationOutcome.Valid, DocumentationValidator.Validate(fixture.Root).Outcome);
        WritePair(fixture, externalSuccessor ? oldTarget : "x:ignored/../" + oldTarget,
            externalSuccessor ? "x:ignored/../" + newTarget : newTarget);
        var result = DocumentationValidator.Validate(fixture.Root);
        Assert.Equal(ValidationOutcome.Invalid, result.Outcome);
        Assert.Null(result.ToolError);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V211);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V214);
        if (externalSuccessor)
        {
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V215);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V216);
        }
        else Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V216);
        Assert.Equal(AuthorityState.NonCurrent, result.Documents.Single(document => document.DecisionId == "ADR-001").AuthorityState);
        Assert.Equal(AuthorityState.Current, result.Documents.Single(document => document.DecisionId == "ADR-002").AuthorityState);
    }

    [Theory]
    [InlineData("#self", ValidationDiagnosticCode.LC42V212)]
    [InlineData("ADR-002-new.md)\n[again](ADR-002-new.md", ValidationDiagnosticCode.LC42V213)]
    public void InvalidLocalTargetsCannotSupplySuccessor(string destination, ValidationDiagnosticCode expected)
    {
        using var fixture = Project();
        fixture.Write("decisions/ADR-002-new.md", ContractCoverageTests.Adr("ADR-002", "Accepted", "## Supersedes\n\n[old](ADR-001-old.md)\n"));
        fixture.Write("decisions/ADR-001-old.md", ContractCoverageTests.Adr("ADR-001", "Superseded",
            $"## Superseded by\n\n[target]({destination})\n"));
        var result = DocumentationValidator.Validate(fixture.Root);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == expected);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V214);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V216);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmailAutolinksRemainExternalAndAreRejectedOnlyAsRelationships(bool relationship)
    {
        using var fixture = Project();
        fixture.Write("decisions/ADR-001-old.md", ContractCoverageTests.Adr("ADR-001", "Accepted",
            $"## {(relationship ? "Related decisions" : "Contact")}\n\n<reviewer@example.com>\n"));
        var result = DocumentationValidator.Validate(fixture.Root);
        Assert.Null(result.ToolError);
        if (relationship)
        {
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(ValidationDiagnosticCode.LC42V211, diagnostic.Code);
            Assert.Equal("mailto:reviewer@example.com", diagnostic.OriginalDestination);
        }
        else Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code is ValidationDiagnosticCode.LC42V105 or ValidationDiagnosticCode.LC42V106);
    }

    [Theory]
    [InlineData("ADR-002: Setext\n==============", false)]
    [InlineData("# ADR-002: ATX\n\nAnother title\n=============", true)]
    [InlineData("ADR-003: Mismatch\n=================", true)]
    public void AdrCountsAllCommonMarkH1sAndPreservesIndependentAuthority(string title, bool invalid)
    {
        using var fixture = Project();
        WritePair(fixture, "ADR-001-old.md", "ADR-002-new.md");
        var adr = ContractCoverageTests.Adr("ADR-002", "Accepted");
        fixture.Write("decisions/ADR-002-new.md", title + adr[adr.IndexOf('\n')..]);
        var result = DocumentationValidator.Validate(fixture.Root);
        Assert.Equal(invalid, result.Diagnostics.Any(diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V202));
        Assert.Equal(AuthorityState.Current, result.Documents.Single(document => document.DecisionId == "ADR-002").AuthorityState);
        if (invalid)
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code is ValidationDiagnosticCode.LC42V214 or ValidationDiagnosticCode.LC42V215 or ValidationDiagnosticCode.LC42V216);
        else
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V214);
        fixture.Write("decisions/ADR-002-new.md", title + adr[adr.IndexOf('\n')..] + "\n## Supersedes\n\n[old](ADR-001-old.md)\n");
        if (!invalid) Assert.Equal(ValidationOutcome.Valid, DocumentationValidator.Validate(fixture.Root).Outcome);
    }

    [Fact]
    public void ProjectStillRequiresAtxH1()
    {
        using var fixture = Project();
        fixture.Write("project.md", "Project\n=======\n\nDocumentation contract: llamarc42/v0.1\n\nPurpose.\n");
        Assert.Equal(ValidationDiagnosticCode.LC42V007, Assert.Single(DocumentationValidator.Validate(fixture.Root).Diagnostics).Code);
    }

    private static void WritePair(DiscoveryFixture fixture, string predecessor, string successor)
    {
        fixture.Write("decisions/ADR-001-old.md", ContractCoverageTests.Adr("ADR-001", "Superseded", $"## Superseded by\n\n[next]({successor})\n"));
        fixture.Write("decisions/ADR-002-new.md", ContractCoverageTests.Adr("ADR-002", "Accepted", $"## Supersedes\n\n[old]({predecessor})\n"));
    }

    private static DiscoveryFixture Project()
    {
        var fixture = new DiscoveryFixture();
        fixture.Write("project.md", "# Project\n\nDocumentation contract: llamarc42/v0.1\n\nPurpose.\n");
        return fixture;
    }
}
