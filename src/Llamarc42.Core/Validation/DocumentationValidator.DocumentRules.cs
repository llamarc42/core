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
    private static void ValidatePaths(IReadOnlyList<DocumentState> sources, List<ValidationDiagnostic> diagnostics)
    {
        foreach (var source in sources)
        {
            foreach (var segment in source.Entry.SourcePath.Split('/'))
            {
                if (segment.Any(character => character < ' ' || character == '\u007F' || "<>:\"\\|?*".Contains(character, StringComparison.Ordinal)))
                {
                    diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V101,
                        $"Path segment '{segment}' contains a prohibited portable character."));
                }

                if (segment.EndsWith(' ') || segment.EndsWith('.'))
                {
                    diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V102,
                        $"Path segment '{segment}' ends in a prohibited space or period."));
                }

                var stem = segment.Split('.')[0];
                if (ReservedNamePattern().IsMatch(stem))
                {
                    diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V103,
                        $"Path segment '{segment}' uses reserved portable filename stem '{stem}'."));
                }
            }
        }

        foreach (var collision in sources.GroupBy(source => Fold(source.Entry.Path)).Where(group => group.Count() > 1))
        {
            var members = collision.Select(source => source.Entry.SourcePath).ToArray();
            diagnostics.Add(new ValidationDiagnostic(ValidationDiagnosticCode.LC42V104,
                $"Document identities collide after NFC normalization and Unicode default case-folding: {string.Join(", ", members)}.",
                path: collision.First().Entry.Path, relatedSourcePaths: members));
        }
    }

    private static void ValidateProject(DocumentState source, List<ValidationDiagnostic> diagnostics)
    {
        if (source.Text is null)
        {
            return;
        }

        var candidates = DeclarationCandidates(source.Text).Where(candidate => source.IsTopLevelParagraphLine(candidate.Line - 1)).ToArray();
        if (candidates.Length == 0)
        {
            diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V003,
                "project.md requires exactly 'Documentation contract: llamarc42/v0.1'."));
        }
        else if (candidates.Length > 1)
        {
            diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V004,
                $"project.md has multiple documentation-contract declarations at lines {string.Join(", ", candidates.Select(item => item.Line))}."));
        }
        else if (!DeclarationPattern().IsMatch(candidates[0].Text))
        {
            var unsupported = UnsupportedDeclarationPattern().IsMatch(candidates[0].Text);
            diagnostics.Add(source.Diagnostic(
                unsupported ? ValidationDiagnosticCode.LC42V006 : ValidationDiagnosticCode.LC42V005,
                unsupported
                    ? $"Unsupported documentation contract '{candidates[0].Text.Split(':', 2)[1].Trim()}' at line {candidates[0].Line}; this validator supports llamarc42/v0.1."
                    : $"Malformed documentation contract at line {candidates[0].Line}; require exact syntax 'Documentation contract: llamarc42/v0.1'.",
                candidates[0].Line));
        }

        var headings = source.Headings(1).ToArray();
        if (!headings.Any(heading => heading.Text.Length > 0))
        {
            diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V007,
                "project.md requires a non-empty level-one ATX heading."));
        }

        if (!HasAuthoredBody(source))
        {
            diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V008,
                "project.md requires a non-empty authored body line; substantive adequacy remains human review."));
        }

        foreach (var placeholder in new[]
        {
            "# TODO: Project name",
            "TODO: Replace this line with a concise statement of project purpose or scope.",
        })
        {
            foreach (var line in Enumerable.Range(0, source.Lines.Length).Where(line => source.Lines[line] == placeholder))
            {
                diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V009,
                    $"Initialization placeholder remains at line {line + 1}; replace it with authored content.", line + 1));
            }
        }
    }

    private static void ValidateSpecification(DocumentState source, List<ValidationDiagnostic> diagnostics)
    {
        var fields = source.HeaderFields("Status").ToArray();
        if (fields.Length > 1)
        {
            source.Authority = AuthorityState.Indeterminate;
            diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V150,
                $"Specification has multiple header Status fields at lines {string.Join(", ", fields.Select(field => field.Line))}."));
        }
        else if (fields.Length == 1 && fields[0].Value is not ("Proposed" or "Accepted"))
        {
            source.Authority = AuthorityState.Indeterminate;
            diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V151,
                $"Specification status '{fields[0].Value}' is invalid; allowed values are Proposed and Accepted.", fields[0].Line));
        }
        else if (fields.Length == 1)
        {
            source.LifecycleStatus = fields[0].Value;
            source.Authority = fields[0].Value == "Accepted" ? AuthorityState.Current : AuthorityState.NonCurrent;
        }
    }

    private static void ValidateDecisionForm(DocumentState source, List<ValidationDiagnostic> diagnostics)
    {
        var identifier = source.Entry.DecisionId!;
        var matchingHeadings = source.Headings(1).Where(heading => IdentifierToken(heading.Text, identifier)).ToArray();
        if (matchingHeadings.Length != 1 || source.Headings(1).Count() != 1)
        {
            diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V202,
                $"ADR requires exactly one level-one heading containing token {identifier}."));
        }

        var statuses = source.Sections("Status").ToArray();
        if (statuses.Length == 0 || statuses.Length == 1 && statuses[0].Content.Count == 0)
        {
            source.Authority = AuthorityState.Indeterminate;
            diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V203,
                "ADR requires one non-empty ## Status section."));
        }
        else if (statuses.Length != 1 || statuses[0].Content.Count != 1)
        {
            source.Authority = AuthorityState.Indeterminate;
            diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V204,
                $"ADR has ambiguous Status structure at lines {string.Join(", ", statuses.Select(section => section.Line))}."));
        }
        else
        {
            var status = statuses[0].Content[0].Text.Trim();
            if (status is not ("Proposed" or "Accepted" or "Rejected" or "Withdrawn" or "Superseded"))
            {
                source.Authority = AuthorityState.Indeterminate;
                diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V205,
                    $"ADR status '{status}' is invalid; allowed values are Proposed, Accepted, Rejected, Withdrawn, and Superseded.",
                    statuses[0].Content[0].Line));
            }
            else
            {
                source.LifecycleStatus = status;
                source.Authority = status == "Accepted" ? AuthorityState.Current : AuthorityState.NonCurrent;
            }
        }

        ValidateRequiredSection(source, "Context", ValidationDiagnosticCode.LC42V206, diagnostics);
        var decisionSections = source.Sections("Decision").Concat(source.Sections("Proposed decision")).ToArray();
        if (decisionSections.Length != 1 || decisionSections[0].Content.Count == 0)
        {
            diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V207,
                "ADR requires exactly one non-empty ## Decision or ## Proposed decision section."));
        }

        ValidateRequiredSection(source, "Consequences", ValidationDiagnosticCode.LC42V208, diagnostics);
        foreach (var heading in RelationshipHeadings)
        {
            var sections = source.Sections(heading).ToArray();
            if (sections.Length > 1)
            {
                diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V210,
                    $"Relationship heading ## {heading} occurs more than once at lines {string.Join(", ", sections.Select(section => section.Line))}."));
            }
        }
    }

    private static void ValidateDecisionIdentities(IEnumerable<DocumentState> decisions, List<ValidationDiagnostic> diagnostics)
    {
        foreach (var duplicate in decisions.GroupBy(source => source.Entry.DecisionId).Where(group => group.Count() > 1))
        {
            diagnostics.Add(new ValidationDiagnostic(ValidationDiagnosticCode.LC42V201,
                $"Decision identifier {duplicate.Key} is used by multiple paths.",
                relatedSourcePaths: duplicate.Select(source => source.Entry.SourcePath)));
        }
    }

    private static void ValidateIteration(DocumentState source, List<ValidationDiagnostic> diagnostics)
    {
        var path = source.Entry.Path;
        if (!path.StartsWith("history/iterations/", StringComparison.Ordinal)) return;
        var segments = path.Split('/');
        var file = segments[^1];
        var year = segments.Length > 3 ? segments[2] : string.Empty;
        var asciiYear = year.Length == 4 && year.All(char.IsAsciiDigit);

        var runes = file.EnumerateRunes().ToArray();
        var daily = runes.Length >= 10 && file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
            runes[4].Value == '-' && runes[7].Value == '-' &&
            DateDigitPositions.All(index => Rune.IsDigit(runes[index]));
        var grammar = DailyExactPattern().IsMatch(file);
        if (daily && !grammar)
        {
            diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V109,
                $"Daily iteration filename '{file}' requires ASCII YYYY-MM-DD, optional lowercase kebab topic, and exact .md."));
        }

        if (source.Entry.SourcePath.EndsWith(".md", StringComparison.Ordinal) &&
            (segments.Length != 4 || !asciiYear || !grammar))
        {
            diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V300,
                "Iteration record should use history/iterations/YYYY/YYYY-MM-DD[-topic].md."));
        }

        if (!daily || !grammar) return;
        var fileYear = int.Parse(file.AsSpan(0, 4), CultureInfo.InvariantCulture);
        var month = int.Parse(file.AsSpan(5, 2), CultureInfo.InvariantCulture);
        var day = int.Parse(file.AsSpan(8, 2), CultureInfo.InvariantCulture);
        if (fileYear < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(fileYear, month))
        {
            diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V301,
                $"Daily iteration date '{file[..10]}' is not a valid Gregorian date."));
        }

        var immediateParent = segments[^2];
        if (immediateParent.Length == 4 && immediateParent.All(char.IsAsciiDigit) && immediateParent != file[..4])
        {
            diagnostics.Add(source.Diagnostic(ValidationDiagnosticCode.LC42V302,
                $"Iteration filename year {file[..4]} differs from directory year {immediateParent}."));
        }
    }
}
