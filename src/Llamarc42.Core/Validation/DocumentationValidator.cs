using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Llamarc42.Core.Discovery;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Llamarc42.Core.Validation;

/// <summary>Validates a discovered documentation project against llamarc42/v0.1.</summary>
public static partial class DocumentationValidator
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder().UsePreciseSourceLocation().Build();

    /// <summary>Performs deterministic, local, read-only validation from <paramref name="startPath"/>.</summary>
    public static DocumentationValidationResult Validate(string startPath) =>
        Validate(startPath, ProjectDiscovery.Discover, File.ReadAllBytes);

    internal static DocumentationValidationResult Validate(
        string startPath,
        Func<string, ProjectDiscoveryResult> discover,
        Func<string, byte[]> readAllBytes)
    {
        ProjectDiscoveryResult discovery;
        try
        {
            discovery = discover(startPath);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new DocumentationValidationResult(startPath, null, [], [],
                new ValidationToolError(ValidationToolErrorCode.LC42T999,
                    $"Internal validator failure during discovery ({exception.GetType().Name}); conformance was not determined."));
        }
        var diagnostics = MapDiscoveryDiagnostics(discovery);
        if (discovery.Outcome == ProjectDiscoveryOutcome.NotFound)
        {
            diagnostics.Add(Diagnostic(ValidationDiagnosticCode.LC42V001,
                $"No non-link regular project.md was found from supplied start '{discovery.SuppliedPath}'."));
            return Result(discovery, [], diagnostics);
        }

        if (discovery.Outcome == ProjectDiscoveryOutcome.Error)
        {
            return Result(discovery, discovery.Documents.Select(entry => DocumentState.Unreadable(entry).ToDocument()), diagnostics, MapDiscoveryError(discovery));
        }

        var sources = new List<DocumentState>();
        ValidationToolError? readFailure = null;
        foreach (var entry in discovery.Documents)
        {
            var absolutePath = Path.Combine(discovery.ProjectRoot!, entry.SourcePath.Replace('/', Path.DirectorySeparatorChar));
            byte[] bytes;
            try
            {
                bytes = readAllBytes(absolutePath);
            }
            catch (Exception exception) when (IsReadException(exception))
            {
                sources.Add(DocumentState.Unreadable(entry));
                readFailure ??= new ValidationToolError(ValidationToolErrorCode.LC42T003,
                    $"Reading '{entry.SourcePath}' completely failed during document read ({exception.GetType().Name}); correct access and retry.");
                continue;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                sources.AddRange(discovery.Documents.Skip(sources.Count).Select(DocumentState.Unreadable));
                return Result(discovery, ToDocuments(sources), diagnostics,
                    new ValidationToolError(ValidationToolErrorCode.LC42T999,
                        $"Validation failed during document read ({exception.GetType().Name}); conformance was not determined."));
            }

            try
            {
                var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                var source = new DocumentState(entry, StrictUtf8.GetString(bytes, offset, bytes.Length - offset));
                sources.Add(source);
                if (entry.DocumentType == DocumentType.DecisionRecord)
                {
                    ValidateDecisionForm(source, diagnostics);
                }
                else if (entry.DocumentType == DocumentType.CurrentGuidance && entry.Path.StartsWith("specifications/", StringComparison.Ordinal))
                {
                    ValidateSpecification(source, diagnostics);
                }
            }
            catch (DecoderFallbackException)
            {
                var state = DocumentState.Undecodable(entry);
                sources.Add(state);
                diagnostics.Add(state.Diagnostic(ValidationDiagnosticCode.LC42V100,
                    $"'{entry.SourcePath}' is not valid UTF-8 after a complete read."));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                sources.AddRange(discovery.Documents.Skip(sources.Count).Select(DocumentState.Unreadable));
                return Result(discovery, ToDocuments(sources), diagnostics,
                    new ValidationToolError(ValidationToolErrorCode.LC42T999,
                        $"Internal validator failure during Markdown parsing ({exception.GetType().Name}); conformance was not determined."));
            }
        }

        try
        {
            ValidatePaths(sources, diagnostics);
            ValidateProject(sources.Single(source => source.Entry.DocumentType == DocumentType.ProjectDescriptor), diagnostics);
            var decisions = sources.Where(source => source.Entry.DocumentType == DocumentType.DecisionRecord).ToArray();
            ValidateDecisionIdentities(decisions, diagnostics);
            var physical = new PhysicalInventory(
                [.. discovery.Entries.Where(entry => entry.Kind == FileSystemEntryKind.RegularFile).Select(entry => entry.SourcePath)],
                [.. discovery.Entries.Where(entry => entry.Kind == FileSystemEntryKind.Link || entry.IsNestedRoot).Select(entry => entry.SourcePath.Normalize())]);
            ValidateFileCandidates(sources, physical.Files, diagnostics);
            foreach (var yearDirectory in discovery.Entries.Where(entry => entry.Kind == FileSystemEntryKind.Directory && !entry.IsNestedRoot))
            {
                var segments = yearDirectory.SourcePath.Split('/');
                if (segments.Length != 3 || segments[0] != "history" || segments[1] != "iterations") continue;
                var year = segments[2].EnumerateRunes().ToArray();
                if (year.Length == 4 && year.All(Rune.IsDigit) && !segments[2].All(char.IsAsciiDigit))
                {
                    diagnostics.Add(new ValidationDiagnostic(ValidationDiagnosticCode.LC42V109,
                        $"Iteration year directory '{segments[2]}' requires four ASCII digits.",
                        yearDirectory.SourcePath.Normalize(), yearDirectory.SourcePath));
                }
            }
            ValidateLinks(sources, physical, decisions, diagnostics);
            ValidateRelationships(decisions, diagnostics);
            ValidateAgents(sources, diagnostics);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Result(discovery, ToDocuments(sources), diagnostics,
                new ValidationToolError(ValidationToolErrorCode.LC42T999,
                    $"Validation failed during rule evaluation ({exception.GetType().Name}); conformance was not determined."));
        }

        return Result(discovery, ToDocuments(sources), diagnostics, readFailure);
    }

    internal static IReadOnlyList<ValidationDiagnostic> ValidatePortablePathsForTesting(params DocumentInventoryEntry[] entries)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        ValidatePaths([.. entries.Select(DocumentState.Unreadable)], diagnostics);
        return diagnostics;
    }

    private static List<ValidationDiagnostic> MapDiscoveryDiagnostics(ProjectDiscoveryResult discovery) =>
        [.. discovery.Issues
            .Where(issue => issue.Code is ProjectDiscoveryIssueCode.RootMarkerLink or ProjectDiscoveryIssueCode.RootMarkerNotRegular)
            .Select(issue => Diagnostic(ValidationDiagnosticCode.LC42V002,
                $"The project.md entry at '{issue.Path}' is {(issue.Code == ProjectDiscoveryIssueCode.RootMarkerLink ? "a link or reparse point" : "non-regular")}; discovery continued toward a valid ancestor."))];

    private static ValidationToolError MapDiscoveryError(ProjectDiscoveryResult discovery)
    {
        var issue = discovery.Issues[^1];
        var code = issue.Code is ProjectDiscoveryIssueCode.StartPathInvalid or
            ProjectDiscoveryIssueCode.StartPathNotFound or
            ProjectDiscoveryIssueCode.StartPathInaccessible or
            ProjectDiscoveryIssueCode.StartPathLink or
            ProjectDiscoveryIssueCode.ParentInspectionFailed
            ? ValidationToolErrorCode.LC42T001
            : ValidationToolErrorCode.LC42T002;
        return new ValidationToolError(code,
            $"Supplied start '{discovery.SuppliedPath}', affected path '{issue.Path}': {issue.Message}" +
            (code == ValidationToolErrorCode.LC42T002 && discovery.ProjectRoot is null
                ? " Root discovery stopped; no ancestor was selected through the uninspected condition."
                : string.Empty));
    }

}
