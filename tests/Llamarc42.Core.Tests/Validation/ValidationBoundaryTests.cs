using System.Diagnostics;
using System.Text.Json;
using Llamarc42.Core.Discovery;
using Llamarc42.Core.Tests.Discovery;
using Llamarc42.Core.Validation;

namespace Llamarc42.Core.Tests.Validation;

public sealed class ValidationBoundaryTests
{
    [Fact]
    public void HtmlBodySingleLetterSchemesAndRepeatedIdentifierTokensUseParsedMeaning()
    {
        using var fixture = Project();
        fixture.Write("project.md", "# Project\n\nDocumentation contract: llamarc42/v0.1\n\n<div>Authored purpose.</div>\n");
        fixture.Write("guides/guide.md", "[scheme](x:opaque)\n\n## Amends\n\n[external](https://example.invalid)\n");
        fixture.Write("decisions/ADR-001-one.md", ContractCoverageTests.Adr("prefixADR-001 then ADR-001", "Proposed"));
        Assert.Equal(ValidationOutcome.Valid, DocumentationValidator.Validate(fixture.Root).Outcome);
        fixture.Write("guides/guide.md", "<file:///outside.md>\n");
        Assert.Contains(DocumentationValidator.Validate(fixture.Root).Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V105);
    }

    [Theory]
    [InlineData("```\nStatus: Proposed\n```\n")]
    [InlineData("    Status: Proposed\n")]
    [InlineData("> Status: Proposed\n")]
    [InlineData("- Status: Proposed\n")]
    [InlineData("<!--\nStatus: Proposed\n-->\n")]
    [InlineData("## Later\nStatus: Proposed\n")]
    [InlineData("Field | Other\n--- | ---\nStatus: Proposed | example\n")]
    public void MetadataIgnoresExcludedMarkdownContexts(string context)
    {
        using var fixture = Project();
        fixture.Write("specifications/spec.md", "# Spec\n\n" + context);
        var result = DocumentationValidator.Validate(fixture.Root);
        Assert.Null(result.ToolError);
        Assert.Equal(AuthorityState.Current, result.Documents.Single(document => document.SourcePath == "specifications/spec.md").AuthorityState);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code is ValidationDiagnosticCode.LC42V150 or ValidationDiagnosticCode.LC42V151);
    }

    [Theory]
    [InlineData("```\nDocumentation contract: llamarc42/v0.1\n```\n")]
    [InlineData("    Documentation contract: llamarc42/v0.1\n")]
    [InlineData("> Documentation contract: llamarc42/v0.1\n")]
    [InlineData("- Documentation contract: llamarc42/v0.1\n")]
    [InlineData("`Documentation contract: llamarc42/v0.1`\n")]
    [InlineData("\"Documentation contract: llamarc42/v0.1\"\n")]
    [InlineData("<!--\nDocumentation contract: llamarc42/v0.1\n-->\n")]
    [InlineData("Field | Other\n--- | ---\nDocumentation contract: llamarc42/v0.1 | example\n")]
    public void DeclarationExamplesDoNotSelectContract(string example)
    {
        using var fixture = Project();
        fixture.Write("project.md", "# Project\n\nPurpose.\n\n" + example);
        Assert.Contains(DocumentationValidator.Validate(fixture.Root).Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V003);
    }

    [Theory]
    [InlineData("```\nOnly code.\n```\n")]
    [InlineData("    Only code.\n")]
    [InlineData("<!--\nOnly a comment.\n-->\n")]
    public void CodeAndCommentsDoNotSupplyProjectPurpose(string body)
    {
        using var fixture = Project();
        fixture.Write("project.md", "# Project\n\nDocumentation contract: llamarc42/v0.1\n\n" + body);
        Assert.Contains(DocumentationValidator.Validate(fixture.Root).Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V008);
    }

    [Theory]
    [InlineData("%ZZ.md")]
    [InlineData("%C3%28.md")]
    [InlineData("%FF.md")]
    [InlineData("%2Fetc/passwd")]
    [InlineData("guides%5Cguide.md")]
    [InlineData("file:///tmp/outside.md")]
    [InlineData("C:/outside.md")]
    public void InvalidLocalLocatorDecodingEmitsV105(string target)
    {
        using var fixture = Project();
        fixture.Write("guides/links.md", $"[bad]({target})\n");
        var result = DocumentationValidator.Validate(fixture.Root);
        Assert.Null(result.ToolError);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V105);
    }

    [Fact]
    public void ReferencesUseWholeDocumentDefinitionsAndIgnoreImagesCodeAndHtml()
    {
        using var fixture = Project();
        fixture.Write("decisions/ADR-001-one.md", ContractCoverageTests.Adr("ADR-001", "Proposed",
            "## Related decisions\n\n[other][two]\n\n## Later\n\n[two]: ADR-002-two.md\n"));
        fixture.Write("decisions/ADR-002-two.md", ContractCoverageTests.Adr("ADR-002", "Proposed"));
        fixture.Write("guides/links.md", "![image](missing.png)\n`[code](missing.md)`\n\n```\n[fenced](missing.md)\n```\n\n<a href=\"missing.md\">HTML</a>\n\n[external](https://example.invalid/never-fetch)\n");
        var before = Snapshot(fixture.Root);
        var result = DocumentationValidator.Validate(fixture.Root);
        Assert.Equal(ValidationOutcome.Valid, result.Outcome);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(before, Snapshot(fixture.Root));
    }

    [Fact]
    public void SupportingAssetsAndNfcReferencesResolveWithoutChangingSourcePaths()
    {
        using var fixture = Project();
        fixture.Write("assets/café.txt", "asset");
        fixture.Write("guides/links.md", "[asset](../assets/cafe%CC%81.txt?download=true#fragment)\n");
        fixture.Write("AGENTS.md", "[asset](assets/café.txt)\n[project](project.md)\n");
        var result = DocumentationValidator.Validate(fixture.Root);
        Assert.Equal(ValidationOutcome.Valid, result.Outcome);
        Assert.DoesNotContain(result.Documents, document => document.SourcePath.EndsWith(".txt", StringComparison.Ordinal));
    }

    [Fact]
    public void LinkedDirectoryAndDescendantTargetsReportExcludedBoundary()
    {
        using var fixture = Project();
        var sibling = fixture.CreateSiblingDirectory();
        File.WriteAllText(Path.Combine(sibling, "outside.md"), "outside");
        var alias = Path.Combine(fixture.Root, "alias");
        CreateDirectoryLink(alias, sibling);
        fixture.Write("guides/links.md", "[outside](../alias/outside.md)\n");
        var result = DocumentationValidator.Validate(fixture.Root);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V107);
        Assert.DoesNotContain(result.Documents, document => document.SourcePath.Contains("alias", StringComparison.Ordinal));
        Assert.Equal(ValidationToolErrorCode.LC42T001, DocumentationValidator.Validate(alias).ToolError?.Code);
        Assert.Equal(ValidationToolErrorCode.LC42T001, DocumentationValidator.Validate(Path.Combine(alias, "outside.md")).ToolError?.Code);
    }

    [Theory]
    [InlineData("Proposed")]
    [InlineData("Rejected")]
    [InlineData("Withdrawn")]
    public void IntendedRelationshipsHaveNoAuthorityEdgeDiagnostics(string status)
    {
        using var fixture = Project();
        fixture.Write("decisions/ADR-001-one.md", ContractCoverageTests.Adr("ADR-001", status,
            "## Supersedes\n\n[two](ADR-002-two.md)\n\n## Amends\n\n[two](ADR-002-two.md)\n"));
        fixture.Write("decisions/ADR-002-two.md", ContractCoverageTests.Adr("ADR-002", "Accepted"));
        var result = DocumentationValidator.Validate(fixture.Root);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(AuthorityState.NonCurrent, result.Documents.Single(document => document.DecisionId == "ADR-001").AuthorityState);
    }

    [Fact]
    public void InvalidEncodingSuppressesContentRulesAndMakesOnlyLifecycleTypesIndeterminate()
    {
        using var fixture = Project();
        foreach (var path in new[] { "specifications/spec.md", "decisions/ADR-001-one.md", "history/old.md", "guides/guide.md" })
            File.WriteAllBytes(fixture.Write(path, ""), [0xFF]);
        var result = DocumentationValidator.Validate(fixture.Root);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(ValidationDiagnosticCode.LC42V100, diagnostic.Code));
        Assert.Equal(2, result.Documents.Count(document => document.AuthorityState == AuthorityState.Indeterminate));
        Assert.Equal(AuthorityState.NonCurrent, result.Documents.Single(document => document.SourcePath == "history/old.md").AuthorityState);
        Assert.Equal(AuthorityState.Current, result.Documents.Single(document => document.SourcePath == "guides/guide.md").AuthorityState);
    }

    [Fact]
    public void AccessDeniedAndInventoryFailuresPreserveEstablishedEvidence()
    {
        using var fixture = Project();
        fixture.Write("specifications/spec.md", "# Spec\nStatus: Accepted\n");
        fixture.Write("z-broken.md", "broken");
        var inventory = DocumentationValidator.Validate(fixture.Root,
            start => ProjectDiscovery.Discover(start, (path, attributes) => Path.GetFileName(path) == "z-broken.md"
                ? throw new IOException("controlled inventory failure") : FileSystemEntryInspector.Inspect(path, attributes)), File.ReadAllBytes);
        Assert.Equal(ValidationToolErrorCode.LC42T002, inventory.ToolError?.Code);
        Assert.Contains(inventory.Documents, document => document.SourcePath == "project.md");
        var access = DocumentationValidator.Validate(fixture.Root, ProjectDiscovery.Discover,
            path => path.EndsWith("spec.md", StringComparison.Ordinal) ? throw new UnauthorizedAccessException("controlled denial") : File.ReadAllBytes(path));
        Assert.Equal(ValidationToolErrorCode.LC42T003, access.ToolError?.Code);
        Assert.Equal(3, access.Documents.Count);
        Assert.DoesNotContain(access.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V100);
    }

    [Theory]
    [InlineData("Straße", "strasse")]
    [InlineData("Σςσ", "σσσ")]
    [InlineData("İ", "i\u0307")]
    [InlineData("ﬃ", "ffi")]
    [InlineData("\U00010400", "\U00010428")]
    public void FullDefaultCaseFoldingHasPinnedUnicodeMappings(string source, string expected) =>
        Assert.Equal(expected, UnicodeCaseFolding.Fold(source));

    private static DiscoveryFixture Project()
    {
        var fixture = new DiscoveryFixture();
        fixture.Write("project.md", "# Project\n\nDocumentation contract: llamarc42/v0.1\n\nPurpose.\n");
        return fixture;
    }

    private static string Snapshot(string root) => JsonSerializer.Serialize(Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(path => new { Path = Path.GetRelativePath(root, path), Bytes = File.Exists(path) ? Convert.ToHexString(File.ReadAllBytes(path)) : null }));

    private static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        var info = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, target }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }
}
