using System.Text;
using Llamarc42.Core.Discovery;
using Llamarc42.Core.Tests.Discovery;
using Llamarc42.Core.Validation;

namespace Llamarc42.Core.Tests.Validation;

public sealed class DocumentationValidatorTests
{
    [Fact]
    public void MinimumProjectIsValidAndDeterministic()
    {
        using var fixture = new DiscoveryFixture();
        fixture.Write("project.md", "# Example\n\nDocumentation contract: llamarc42/v0.1\n\nA project purpose.\n");
        var before = File.ReadAllBytes(Path.Combine(fixture.Root, "project.md"));

        var first = DocumentationValidator.Validate(fixture.Root);
        var second = DocumentationValidator.Validate(fixture.Root);

        Assert.Equal(ValidationOutcome.Valid, first.Outcome);
        Assert.Empty(first.Diagnostics);
        Assert.Null(first.ToolError);
        Assert.Equal(first.Documents, second.Documents);
        Assert.Equal(first.Diagnostics.Select(Value), second.Diagnostics.Select(Value));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(fixture.Root, "project.md")));
    }

    [Theory]
    [InlineData("# Example\n\nPurpose.\n", ValidationDiagnosticCode.LC42V003)]
    [InlineData("Documentation contract: llamarc42/v0.1\n\nPurpose.\n", ValidationDiagnosticCode.LC42V007)]
    [InlineData("# Example\n\nDocumentation contract: llamarc42/v0.1\n", ValidationDiagnosticCode.LC42V008)]
    [InlineData("# TODO: Project name\n\nDocumentation contract: llamarc42/v0.1\n\nPurpose.\n", ValidationDiagnosticCode.LC42V009)]
    [InlineData("# Example\n\nDocumentation contract: llamarc42/v0.1\nDocumentation contract: llamarc42/v0.1\n\nPurpose.\n", ValidationDiagnosticCode.LC42V004)]
    [InlineData("# Example\n\nDocumentation Contract: llamarc42/v0.1\n\nPurpose.\n", ValidationDiagnosticCode.LC42V005)]
    [InlineData("# Example\n\nDocumentation contract: llamarc42/v0.2\n\nPurpose.\n", ValidationDiagnosticCode.LC42V006)]
    public void ProjectDescriptorRulesEmitExpectedCode(string project, ValidationDiagnosticCode expected)
    {
        using var fixture = new DiscoveryFixture();
        fixture.Write("project.md", project);

        var result = DocumentationValidator.Validate(fixture.Root);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == expected);
        Assert.Equal(ValidationOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public void Utf8BomAndLineEndingsAreEquivalentWhileInvalidUtf8IsConformanceFailure()
    {
        using var lf = new DiscoveryFixture();
        using var crlf = new DiscoveryFixture();
        var content = "# Example\n\nDocumentation contract: llamarc42/v0.1\n\nPurpose.\n";
        File.WriteAllBytes(lf.Write("project.md", string.Empty), [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(content)]);
        crlf.Write("project.md", content.Replace("\n", "\r\n", StringComparison.Ordinal));

        var lfResult = DocumentationValidator.Validate(lf.Root);
        var crlfResult = DocumentationValidator.Validate(crlf.Root);
        File.WriteAllBytes(Path.Combine(lf.Root, "project.md"), [0xC3, 0x28]);
        var invalid = DocumentationValidator.Validate(lf.Root);

        Assert.Equal(lfResult.Diagnostics.Select(Value), crlfResult.Diagnostics.Select(Value));
        Assert.Contains(invalid.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V100);
        Assert.Equal(AuthorityState.Current, invalid.Documents.Single().AuthorityState);
    }

    [Fact]
    public void SpecificationLifecycleAndUnicodeCollisionDeriveAuthorityPerSource()
    {
        using var fixture = new DiscoveryFixture();
        fixture.Write("project.md", ValidProject);
        fixture.Write("specifications/Straße.md", "# Spec\n\nStatus: Accepted\n\n## Scope\n\nOne.\n");
        fixture.Write("specifications/STRASSE.md", "# Spec\n\nStatus: Proposed\n\n## Scope\n\nTwo.\n");
        fixture.Write("specifications/invalid.md", "# Spec\n\nStatus: Draft\n\n## Scope\n\nThree.\n");

        var result = DocumentationValidator.Validate(fixture.Root);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V104);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V151);
        Assert.Equal(AuthorityState.Current, result.Documents.Single(document => document.SourcePath.EndsWith("Straße.md", StringComparison.Ordinal)).AuthorityState);
        Assert.Equal(AuthorityState.NonCurrent, result.Documents.Single(document => document.SourcePath.EndsWith("STRASSE.md", StringComparison.Ordinal)).AuthorityState);
        Assert.Equal(AuthorityState.Indeterminate, result.Documents.Single(document => document.SourcePath.EndsWith("invalid.md", StringComparison.Ordinal)).AuthorityState);
    }

    [Fact]
    public void LocalLinksUseExactCaseAndReportMalformedMissingAndNestedTargets()
    {
        using var fixture = new DiscoveryFixture();
        fixture.Write("project.md", ValidProject + "\n[bad](../outside.md) [case](guides/Setup.md) [nested](child/guide.md)\n");
        fixture.Write("guides/setup.md", "# Setup\n");
        fixture.Write("child/project.md", ValidProject);
        fixture.Write("child/guide.md", "# Child\n");

        var result = DocumentationValidator.Validate(fixture.Root);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V105);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V106);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V107);
    }

    [Fact]
    public void AdrFormAndRelationshipRulesAggregateWithoutChangingExplicitAuthority()
    {
        using var fixture = new DiscoveryFixture();
        fixture.Write("project.md", ValidProject);
        fixture.Write("decisions/ADR-001-old.md", Adr("ADR-001", "Accepted", "## Superseded by\n\n[ADR-002](ADR-002-new.md)"));
        fixture.Write("decisions/ADR-002-new.md", Adr("ADR-002", "Accepted", "## Supersedes\n\n[ADR-001](ADR-001-old.md)"));

        var result = DocumentationValidator.Validate(fixture.Root);

        Assert.Null(result.ToolError);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V214);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V215);
        Assert.All(result.Documents.Where(document => document.DocumentType == DocumentType.DecisionRecord),
            document => Assert.Equal(AuthorityState.Current, document.AuthorityState));
    }

    [Fact]
    public void IterationAndAgentsRecommendationsHaveContractSeverities()
    {
        using var fixture = new DiscoveryFixture();
        fixture.Write("project.md", ValidProject);
        fixture.Write("history/iterations/2025/2026-02-29-Review.md", "record");
        fixture.Write("history/iterations/2025/2026-02-29.md", "record");
        fixture.Write("AGENTS.md", "[Guide](guides/setup.md)\n[Project](project.md)\n");
        fixture.Write("guides/setup.md", "# Setup\n");

        var result = DocumentationValidator.Validate(fixture.Root);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V109);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V301);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V302 && diagnostic.Severity == ValidationSeverity.Warning);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V400 && diagnostic.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void ControlledReadFailuresReturnT003AndUnexpectedFailuresReturnT999()
    {
        using var fixture = new DiscoveryFixture();
        fixture.Write("project.md", ValidProject);

        var io = DocumentationValidator.Validate(fixture.Root, ProjectDiscovery.Discover, _ => throw new IOException("controlled"));
        var unexpected = DocumentationValidator.Validate(fixture.Root, ProjectDiscovery.Discover, _ => throw new InvalidOperationException("controlled"));

        Assert.Equal(ValidationToolErrorCode.LC42T003, io.ToolError?.Code);
        Assert.Equal(ValidationToolErrorCode.LC42T999, unexpected.ToolError?.Code);
        Assert.Equal(ValidationOutcome.Error, io.Outcome);
        Assert.Equal(ValidationOutcome.Error, unexpected.Outcome);
    }

    private const string ValidProject = "# Example\n\nDocumentation contract: llamarc42/v0.1\n\nPurpose.\n";

    private static string Adr(string id, string status, string relationships = "") => $"""
        # {id}: Example

        ## Status

        {status}

        ## Context

        Context.

        ## Decision

        Decision.

        ## Consequences

        Consequences.
        """ + "\n\n" + relationships + "\n";

    private static object Value(ValidationDiagnostic diagnostic) => new
    {
        diagnostic.Code,
        diagnostic.Severity,
        diagnostic.Path,
        diagnostic.SourcePath,
        diagnostic.Line,
        Related = string.Join('|', diagnostic.RelatedSourcePaths),
    };
}
