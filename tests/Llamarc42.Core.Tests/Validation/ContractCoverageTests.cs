using System.Text;
using System.Text.Json;
using Llamarc42.Core.Discovery;
using Llamarc42.Core.Tests.Discovery;
using Llamarc42.Core.Validation;

namespace Llamarc42.Core.Tests.Validation;

/// <summary>Phase 2 implementation evidence, not VAL-01 independent conformance.</summary>
public sealed class ContractCoverageTests
{
    public static TheoryData<ValidationDiagnosticCode> Codes => [.. Enum.GetValues<ValidationDiagnosticCode>()];

    [Theory]
    [MemberData(nameof(Codes))]
    public void EveryCodeHasEmittingAndNearbyValidEvidence(ValidationDiagnosticCode code)
    {
        using var fixture = new DiscoveryFixture();
        var files = ValidFiles();
        foreach (var file in files) fixture.Write(file.Key, file.Value);
        var valid = DocumentationValidator.Validate(fixture.Root);
        Assert.Null(valid.ToolError);
        Assert.Empty(valid.Diagnostics);
        Assert.Equal(ValidationOutcome.Valid, valid.Outcome);

        if (code is >= ValidationDiagnosticCode.LC42V101 and <= ValidationDiagnosticCode.LC42V104)
        {
            var good = DocumentationValidator.ValidatePortablePathsForTesting(Entry("guides/valid.md"));
            Assert.DoesNotContain(good, diagnostic => diagnostic.Code == code);
            var bad = code switch
            {
                ValidationDiagnosticCode.LC42V101 => new[] { Entry("guides/a?.md") },
                ValidationDiagnosticCode.LC42V102 => [Entry("guides./a.md")],
                ValidationDiagnosticCode.LC42V103 => [Entry("CON.md")],
                _ => [Entry("guides/Straße.md"), Entry("guides/STRASSE.md")],
            };
            Assert.Contains(DocumentationValidator.ValidatePortablePathsForTesting(bad), diagnostic => diagnostic.Code == code);
            return;
        }

        var start = fixture.Root;
        switch (code)
        {
            case ValidationDiagnosticCode.LC42V001:
                File.Delete(Path.Combine(fixture.Root, "project.md"));
                break;
            case ValidationDiagnosticCode.LC42V002:
                fixture.CreateDirectory("child/project.md");
                start = fixture.CreateDirectory("child/below");
                break;
            case ValidationDiagnosticCode.LC42V003: files["project.md"] = "# Project\n\nPurpose.\n"; break;
            case ValidationDiagnosticCode.LC42V004: files["project.md"] += "\nDocumentation contract: llamarc42/v0.1\n"; break;
            case ValidationDiagnosticCode.LC42V005: files["project.md"] = files["project.md"].Replace("Documentation contract:", "Documentation Contract:", StringComparison.Ordinal); break;
            case ValidationDiagnosticCode.LC42V006: files["project.md"] = files["project.md"].Replace("v0.1", "v0.2", StringComparison.Ordinal); break;
            case ValidationDiagnosticCode.LC42V007: files["project.md"] = "Documentation contract: llamarc42/v0.1\n\nPurpose.\n"; break;
            case ValidationDiagnosticCode.LC42V008: files["project.md"] = "# Project\n\nDocumentation contract: llamarc42/v0.1\n"; break;
            case ValidationDiagnosticCode.LC42V009: files["project.md"] = files["project.md"].Replace("# Project", "# TODO: Project name", StringComparison.Ordinal); break;
            case ValidationDiagnosticCode.LC42V100: break;
            case ValidationDiagnosticCode.LC42V105: files["project.md"] += "\n[escape](../outside.md)\n"; break;
            case ValidationDiagnosticCode.LC42V106: files["project.md"] += "\n[case](guides/Guide.md)\n"; break;
            case ValidationDiagnosticCode.LC42V107:
                fixture.Write("child/project.md", files["project.md"]);
                fixture.Write("child/inside.md", "child");
                files["project.md"] += "\n[nested](child/inside.md)\n";
                break;
            case ValidationDiagnosticCode.LC42V109: files["history/iterations/2026/2026-10-16-Bad.md"] = "History."; break;
            case ValidationDiagnosticCode.LC42V150: files["specifications/spec.md"] = "# Spec\n\nStatus: Accepted\nStatus: Proposed\n"; break;
            case ValidationDiagnosticCode.LC42V151: files["specifications/spec.md"] = "# Spec\n\nStatus: Draft\n"; break;
            case ValidationDiagnosticCode.LC42V200: files["decisions/ADR-9-invalid.md"] = "candidate"; break;
            case ValidationDiagnosticCode.LC42V201: files["decisions/ADR-001-duplicate.md"] = files[Old]; break;
            case ValidationDiagnosticCode.LC42V202: files[Old] = files[Old].Replace("# ADR-001:", "# ADR-009:", StringComparison.Ordinal); break;
            case ValidationDiagnosticCode.LC42V203: files[Old] = files[Old].Replace("## Status\n\nSuperseded", "", StringComparison.Ordinal); break;
            case ValidationDiagnosticCode.LC42V204: files[Old] = files[Old].Replace("## Status\n\nSuperseded", "## Status\n\nSuperseded\nAccepted", StringComparison.Ordinal); break;
            case ValidationDiagnosticCode.LC42V205: files[Old] = files[Old].Replace("## Status\n\nSuperseded", "## Status\n\nDraft", StringComparison.Ordinal); break;
            case ValidationDiagnosticCode.LC42V206: files[Old] = files[Old].Replace("Context text.", "", StringComparison.Ordinal); break;
            case ValidationDiagnosticCode.LC42V207: files[Old] = files[Old].Replace("Decision text.", "", StringComparison.Ordinal); break;
            case ValidationDiagnosticCode.LC42V208: files[Old] = files[Old].Replace("Consequences text.", "", StringComparison.Ordinal); break;
            case ValidationDiagnosticCode.LC42V210: files[Old] += "\n## Related decisions\n\n## Related decisions\n"; break;
            case ValidationDiagnosticCode.LC42V211: files[Old] += "\n## Related decisions\n\n[remote](https://example.invalid/never-fetch)\n"; break;
            case ValidationDiagnosticCode.LC42V212: files[Old] += "\n## Related decisions\n\n[self](ADR-001-old.md)\n"; break;
            case ValidationDiagnosticCode.LC42V213: files[Old] += "\n## Related decisions\n\n[new](ADR-002-new.md) [new again](./ADR-002-new.md)\n"; break;
            case ValidationDiagnosticCode.LC42V214:
            case ValidationDiagnosticCode.LC42V215: files[Old] = files[Old].Replace("## Status\n\nSuperseded", "## Status\n\nAccepted", StringComparison.Ordinal); break;
            case ValidationDiagnosticCode.LC42V216: files[Old] = files[Old].Replace("[next](ADR-002-new.md)", "[self](ADR-001-old.md)", StringComparison.Ordinal); break;
            case ValidationDiagnosticCode.LC42V217: files[Target] = Adr("ADR-004", "Accepted"); break;
            case ValidationDiagnosticCode.LC42V218: files[Amender] = files[Amender].Replace("Scope text.", "", StringComparison.Ordinal); break;
            case ValidationDiagnosticCode.LC42V300: files["history/iterations/notes.md"] = "notes"; break;
            case ValidationDiagnosticCode.LC42V301: files["history/iterations/2026/2026-02-29.md"] = "bad date"; break;
            case ValidationDiagnosticCode.LC42V302: files["history/iterations/2025/2026-10-16.md"] = "different year"; break;
            case ValidationDiagnosticCode.LC42V400: files["AGENTS.md"] = "[guide](guides/guide.md)\n[project](project.md)\n"; break;
            default: throw new InvalidOperationException($"Missing fixture for {code}.");
        }

        if (code != ValidationDiagnosticCode.LC42V001)
            foreach (var file in files) fixture.Write(file.Key, file.Value);
        if (code == ValidationDiagnosticCode.LC42V100)
            File.WriteAllBytes(Path.Combine(fixture.Root, "specifications", "spec.md"), [0xFF, 0xFE, 0x20, 0x00]);

        var first = DocumentationValidator.Validate(start);
        var second = DocumentationValidator.Validate(start);
        Assert.Null(first.ToolError);
        Assert.Contains(first.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(code is ValidationDiagnosticCode.LC42V300 or ValidationDiagnosticCode.LC42V302 or ValidationDiagnosticCode.LC42V400
            ? ValidationOutcome.Valid : ValidationOutcome.Invalid, first.Outcome);

        // Same source, CRLF: preserve message values, source lines, authority, and ordering.
        foreach (var file in Directory.EnumerateFiles(fixture.Root, "*.md", SearchOption.AllDirectories))
        {
            if (code == ValidationDiagnosticCode.LC42V100 && file.EndsWith("spec.md", StringComparison.Ordinal)) continue;
            File.WriteAllText(file, File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal));
        }
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(DocumentationValidator.Validate(start)));
    }

    [Theory]
    [InlineData(ValidationToolErrorCode.LC42T001)]
    [InlineData(ValidationToolErrorCode.LC42T002)]
    [InlineData(ValidationToolErrorCode.LC42T003)]
    [InlineData(ValidationToolErrorCode.LC42T999)]
    public void EveryToolErrorHasControlledFailureAndSuccessfulControl(ValidationToolErrorCode code)
    {
        using var fixture = new DiscoveryFixture();
        foreach (var file in ValidFiles()) fixture.Write(file.Key, file.Value);
        Assert.Equal(ValidationOutcome.Valid, DocumentationValidator.Validate(fixture.Root).Outcome);
        var result = code switch
        {
            ValidationToolErrorCode.LC42T001 => DocumentationValidator.Validate(Path.Combine(fixture.Root, "missing")),
            ValidationToolErrorCode.LC42T002 => DocumentationValidator.Validate(fixture.Root,
                start => ProjectDiscovery.Discover(start, (path, attributes) => Path.GetFileName(path) == "project.md"
                    ? throw new IOException("controlled metadata failure") : FileSystemEntryInspector.Inspect(path, attributes)), File.ReadAllBytes),
            ValidationToolErrorCode.LC42T003 => DocumentationValidator.Validate(fixture.Root, ProjectDiscovery.Discover,
                path => path.EndsWith("spec.md", StringComparison.Ordinal) ? InterruptedRead(path) : File.ReadAllBytes(path)),
            _ => DocumentationValidator.Validate(fixture.Root, _ => throw new InvalidOperationException("controlled internal failure"), File.ReadAllBytes),
        };
        Assert.Equal(ValidationOutcome.Error, result.Outcome);
        Assert.Equal(code, result.ToolError?.Code);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCode.LC42V100);
        if (code == ValidationToolErrorCode.LC42T003)
        {
            Assert.Equal(ValidFiles().Count, result.Documents.Count);
            Assert.Equal(AuthorityState.Indeterminate, result.Documents.Single(document => document.SourcePath == "specifications/spec.md").AuthorityState);
            Assert.Equal(AuthorityState.Current, result.Documents.Single(document => document.SourcePath == Amender).AuthorityState);
        }
    }

    private static byte[] InterruptedRead(string path)
    {
        using var stream = File.OpenRead(path);
        _ = stream.ReadByte();
        throw new IOException("controlled interruption after partial read");
    }

    internal static Dictionary<string, string> ValidFiles() => new(StringComparer.Ordinal)
    {
        ["project.md"] = "# Project\n\nDocumentation contract: llamarc42/v0.1\n\nPurpose.\n",
        ["guides/guide.md"] = "# Guide\n\nGuidance.\n",
        ["specifications/spec.md"] = "# Spec\n\nStatus: Accepted\n\n## Scope\n\nScope.\n",
        ["AGENTS.md"] = "[project](project.md)\n",
        ["history/iterations/2026/2026-10-16.md"] = "History.\n",
        [Old] = Adr("ADR-001", "Superseded", "## Superseded by\n\n[next](ADR-002-new.md)\n"),
        [New] = Adr("ADR-002", "Accepted", "## Supersedes\n\n[old](ADR-001-old.md)\n"),
        [Amender] = Adr("ADR-003", "Accepted", "## Amends\n\n[target](ADR-004-target.md)\n\n## Amendment scope\n\nScope text.\n"),
        [Target] = Adr("ADR-004", "Accepted", "## Amended by\n\n[amender](ADR-003-amender.md)\n"),
    };

    internal static string Adr(string id, string status, string relationships = "") =>
        $"# {id}: Example\n\n## Status\n\n{status}\n\n## Context\n\nContext text.\n\n## Decision\n\nDecision text.\n\n## Consequences\n\nConsequences text.\n\n{relationships}";

    private static DocumentInventoryEntry Entry(string path) => new(path, path.Normalize(), DocumentType.Supplemental, null);
    private const string Old = "decisions/ADR-001-old.md";
    private const string New = "decisions/ADR-002-new.md";
    private const string Amender = "decisions/ADR-003-amender.md";
    private const string Target = "decisions/ADR-004-target.md";
}
