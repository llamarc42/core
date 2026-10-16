using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Llamarc42.Core.Discovery;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Llamarc42.Core.Validation;

public static partial class DocumentationValidator
{
    private static void ValidateLinks(
        IReadOnlyList<DocumentState> sources,
        PhysicalInventory physical,
        IReadOnlyList<DocumentState> decisions,
        List<ValidationDiagnostic> diagnostics)
    {
        foreach (var source in sources.Where(source => source.Text is not null))
        {
            foreach (var link in source.Links())
            {
                if (link.Destination.StartsWith('#') && link.RelationshipHeading is null)
                {
                    continue;
                }

                if (IsExternal(link.Destination))
                {
                    if (link.RelationshipHeading is not null)
                    {
                        diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V211,
                            $"Relationship under ## {link.RelationshipHeading} has external target '{link.Destination}'.",
                            link.Line, link.Destination));
                    }

                    continue;
                }

                if (!TryResolveDestination(source.Entry.SourcePath, link.Destination, out var resolved, out var reason))
                {
                    diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V105,
                        $"Local destination '{link.Destination}' is invalid: {reason}.", link.Line, link.Destination));
                    continue;
                }

                var exact = physical.Files.Where(file => string.Equals(file.Normalize(), resolved, StringComparison.Ordinal)).ToArray();
                if (exact.Length != 1)
                {
                    var excluded = physical.ExcludedBoundaries.FirstOrDefault(boundary =>
                        resolved == boundary || resolved.StartsWith(boundary + "/", StringComparison.Ordinal));
                    if (excluded is not null)
                    {
                        diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V107,
                            $"Local destination '{link.Destination}' crosses excluded link or nested-project boundary '{excluded}'.",
                            link.Line, link.Destination));
                        continue;
                    }

                    var likely = physical.Files.Where(file => string.Equals(Fold(file.Normalize()), Fold(resolved), StringComparison.Ordinal)).ToArray();
                    diagnostics.Add(new ValidationDiagnostic(ValidationDiagnosticCode.LC42V106,
                        $"Local destination '{link.Destination}' has no unique exact-case NFC match for '{resolved}'.",
                        source.Entry.Path, source.Entry.SourcePath, link.Line, likely, link.Destination));
                    continue;
                }

                if (link.RelationshipHeading is not null)
                {
                    var target = decisions.SingleOrDefault(decision => decision.Entry.SourcePath == exact[0]);
                    if (target is null)
                    {
                        diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V211,
                            $"Relationship under ## {link.RelationshipHeading} targets non-decision '{link.Destination}'.", link.Line, link.Destination));
                    }
                    else if (target == source)
                    {
                        diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V212,
                            $"Relationship under ## {link.RelationshipHeading} targets the same ADR.", link.Line, link.Destination));
                    }
                    else
                    {
                        source.ValidatedRelationships.Add((link.RelationshipHeading, target));
                    }
                }
            }

            foreach (var section in RelationshipHeadings.Where(_ => source.Entry.DocumentType == DocumentType.DecisionRecord)
                .SelectMany(heading => source.Sections(heading)))
            {
                foreach (var duplicate in section.Links
                    .Where(link => !IsExternal(link.Destination))
                    .GroupBy(link => TryResolveDestination(source.Entry.SourcePath, link.Destination, out var resolved, out _)
                        ? resolved : link.Destination, StringComparer.Ordinal).Where(group => group.Count() > 1))
                {
                    diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V213,
                        $"Relationship target '{duplicate.Key}' occurs more than once under ## {section.Name} at lines {string.Join(", ", duplicate.Select(link => link.Line))}.", duplicate.First().Line, duplicate.Key));
                }
            }
        }
    }

    private static void ValidateFileCandidates(
        IReadOnlyList<DocumentState> sources,
        IReadOnlyList<string> files,
        List<ValidationDiagnostic> diagnostics)
    {
        foreach (var path in files.Where(path => path.StartsWith("decisions/", StringComparison.Ordinal)))
        {
            var name = Path.GetFileName(path);
            if ((name.StartsWith("ADR-", StringComparison.Ordinal) || name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) &&
                !DecisionCandidatePattern().IsMatch(path))
            {
                diagnostics.Add(new ValidationDiagnostic(ValidationDiagnosticCode.LC42V200,
                    $"ADR candidate '{path}' must match decisions/ADR-NNN-lowercase-kebab.md.",
                    path.Normalize(), sources.FirstOrDefault(source => source.Entry.SourcePath == path)?.Entry.SourcePath ?? path));
            }
        }

        foreach (var path in files.Where(path => path.StartsWith("history/iterations/", StringComparison.Ordinal)))
        {
            ValidateIteration(DocumentState.Unreadable(new DocumentInventoryEntry(path, path.Normalize(), DocumentType.HistoricalRecord, null)), diagnostics);
        }
    }

    private static void ValidateRelationships(IReadOnlyList<DocumentState> decisions, List<ValidationDiagnostic> diagnostics)
    {
        var edges = decisions.ToDictionary(source => source, source => RelationshipHeadings.ToDictionary(
            heading => heading, heading => source.ResolvedRelationships(heading).ToArray()));
        static bool Eligible(DocumentState source) => source.IdentifierValid && source.LifecycleStatus is not null;
        var incomingEdges = decisions.ToDictionary(source => source, _ => new List<DocumentState>());
        foreach (var source in decisions.Where(source => Eligible(source) && source.LifecycleStatus is "Accepted" or "Superseded"))
            foreach (var target in edges[source]["Supersedes"]) incomingEdges[target].Add(source);

        foreach (var source in decisions.Where(source => Eligible(source) && source.LifecycleStatus is "Accepted" or "Superseded"))
        {
            var successors = edges[source]["Superseded by"];
            if (successors.All(Eligible) && (source.LifecycleStatus == "Superseded" && successors.Length != 1 ||
                source.LifecycleStatus == "Accepted" && successors.Length > 0 ||
                successors.Any(target => target.LifecycleStatus is not null &&
                    !edges[target]["Supersedes"].Contains(source))))
            {
                diagnostics.Add(new ValidationDiagnostic(ValidationDiagnosticCode.LC42V214,
                    $"ADR '{source.Entry.SourcePath}' requires Superseded status, one declared successor, and reciprocal Supersedes metadata.",
                    source.Entry.Path, source.Entry.SourcePath,
                    relatedSourcePaths: successors.Select(target => target.Entry.SourcePath)));
            }

            foreach (var predecessor in edges[source]["Supersedes"])
            {
                if (!Eligible(predecessor)) continue;
                if (predecessor.LifecycleStatus != "Superseded" ||
                    !edges[predecessor]["Superseded by"].Contains(source))
                {
                    diagnostics.Add(new ValidationDiagnostic(ValidationDiagnosticCode.LC42V215,
                        $"'{source.Entry.SourcePath}' supersedes '{predecessor.Entry.SourcePath}': require Superseded predecessor and reciprocal Superseded by.",
                        source.Entry.Path, source.Entry.SourcePath,
                        relatedSourcePaths: [predecessor.Entry.SourcePath]));
                }
            }

            foreach (var heading in new[] { "Amends", "Amended by" })
            {
                if (source.LifecycleStatus != "Accepted") continue;
                var inverse = heading == "Amends" ? "Amended by" : "Amends";
                foreach (var target in edges[source][heading])
                {
                    if (!Eligible(target)) continue;
                    if (source.LifecycleStatus != "Accepted" || target.LifecycleStatus != "Accepted" ||
                        !edges[target][inverse].Contains(source))
                    {
                        diagnostics.Add(new ValidationDiagnostic(ValidationDiagnosticCode.LC42V217,
                            $"'{source.Entry.SourcePath}' {heading} '{target.Entry.SourcePath}': require Accepted endpoints and reciprocal {inverse}.",
                            source.Entry.Path, source.Entry.SourcePath,
                            relatedSourcePaths: [target.Entry.SourcePath]));
                    }
                }
            }

            if (source.LifecycleStatus == "Accepted" && source.Sections("Amends").SelectMany(section => section.Links).Any())
            {
                var scopes = source.Sections("Amendment scope").ToArray();
                if (scopes.Length != 1 || scopes[0].Content.Count == 0)
                {
                    diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V218,
                        "Accepted amending ADR requires exactly one non-empty ## Amendment scope section."));
                }
            }
        }

        foreach (var start in decisions.Where(source => Eligible(source) && source.LifecycleStatus == "Superseded"))
        {
            var visited = new HashSet<DocumentState>();
            var current = start;
            var invalid = false;
            while (Eligible(current))
            {
                if (!visited.Add(current))
                {
                    invalid = true;
                    break;
                }

                var next = edges[current]["Superseded by"];
                if (next.Any(target => !Eligible(target))) break;
                var incoming = incomingEdges[current].ToArray();
                if (incoming.Length > 1 || next.Length > 1)
                {
                    foreach (var item in next.Concat(incoming)) visited.Add(item);
                    invalid = true;
                    break;
                }

                if (current.LifecycleStatus == "Accepted" && next.Length == 0) break;
                if (current.LifecycleStatus != "Superseded" || next.Length != 1)
                {
                    invalid = true;
                    break;
                }

                current = next[0];
            }

            if (invalid)
            {
                diagnostics.Add(new ValidationDiagnostic(ValidationDiagnosticCode.LC42V216,
                    $"Supersession chain from '{start.Entry.SourcePath}' cycles, branches, or fails to terminate at exactly one Accepted ADR.",
                    start.Entry.Path, start.Entry.SourcePath,
                    relatedSourcePaths: visited.Select(source => source.Entry.SourcePath)));
            }
        }
    }

    private static void ValidateAgents(IEnumerable<DocumentState> sources, List<ValidationDiagnostic> diagnostics)
    {
        var agents = sources.SingleOrDefault(source => source.Entry.Path == "AGENTS.md" && source.Text is not null);
        if (agents is null)
        {
            return;
        }

        var local = agents.Links().Where(link => !IsExternal(link.Destination) && !link.Destination.StartsWith('#'))
            .Select(link => TryResolveDestination("AGENTS.md", link.Destination, out var resolved, out _) ? resolved : string.Empty)
            .Where(path => path.EndsWith(".md", StringComparison.Ordinal)).ToArray();
        if (local.Length == 0 || local[0] != "project.md")
        {
            diagnostics.Add(agents.Diagnostic(ValidationDiagnosticCode.LC42V400,
                "AGENTS.md should make project.md its first local Markdown-document link."));
        }
    }
}
