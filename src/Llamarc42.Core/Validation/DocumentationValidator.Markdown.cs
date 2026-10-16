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
    private static IEnumerable<(string Text, int Line)> DeclarationCandidates(string text)
    {
        var lines = SplitLines(text);
        var fence = '\0';
        var fenceLength = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.TrimStart(' ');
            var indent = line.Length - trimmed.Length;
            if (indent <= 3 && trimmed.Length >= 3 && trimmed[0] is '`' or '~')
            {
                var count = trimmed.TakeWhile(character => character == trimmed[0]).Count();
                if (fence == '\0' && count >= 3)
                {
                    fence = trimmed[0];
                    fenceLength = count;
                    continue;
                }

                if (fence == trimmed[0] && count >= fenceLength && trimmed[count..].Trim(' ', '\t').Length == 0)
                {
                    fence = '\0';
                    continue;
                }
            }

            if (fence != '\0' || line.StartsWith('\t') || indent >= 4 || (indent <= 3 && trimmed.StartsWith('>')))
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var field = line[..colon].TrimEnd(' ', '\t').TrimStart(' ');
            if (field.Equals("Documentation contract", StringComparison.OrdinalIgnoreCase))
            {
                yield return (line, index + 1);
            }
        }
    }

    private static bool HasAuthoredBody(DocumentState source)
    {
        var headerLines = source.HeaderFields(null).Select(field => field.Line - 1).ToHashSet();
        return source.Document.Descendants<LeafBlock>().Where(block => block is ParagraphBlock or HtmlBlock).Any(block =>
            Enumerable.Range(block.Line, CountSourceLines(source.Text!, block.Span.Start, block.Span.End))
                .Any(line => line < source.Lines.Length && !headerLines.Contains(line) &&
                    !string.IsNullOrWhiteSpace(source.CommentFreeLines[line])));
    }

    private static int CountSourceLines(string text, int start, int end) =>
        text.AsSpan(start, Math.Max(0, end - start + 1)).Count('\n') + 1;

    private static void ValidateRequiredSection(DocumentState source, string name, ValidationDiagnosticCode code, List<ValidationDiagnostic> diagnostics)
    {
        var sections = source.Sections(name).ToArray();
        if (sections.Length != 1 || sections[0].Content.Count == 0)
        {
            diagnostics.Add(source.Diagnostic(code, $"ADR requires exactly one non-empty ## {name} section."));
        }
    }

    private static bool TryResolveDestination(string sourcePath, string destination, out string resolved, out string reason)
    {
        resolved = string.Empty;
        reason = string.Empty;
        var locator = destination.Split(['?', '#'], 2)[0];
        if (locator.Contains('\\') || locator.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
            locator.StartsWith('/') || DrivePathPattern().IsMatch(locator))
        {
            reason = "absolute, file, and backslash paths are prohibited";
            return false;
        }

        for (var index = 0; index < locator.Length; index++)
        {
            if (locator[index] == '%' && (index + 2 >= locator.Length ||
                !Uri.IsHexDigit(locator[index + 1]) || !Uri.IsHexDigit(locator[index + 2])))
            {
                reason = "percent encoding is invalid";
                return false;
            }

            if (locator[index] == '%') index += 2;
        }

        try
        {
            var decoded = new StringBuilder();
            for (var index = 0; index < locator.Length;)
            {
                if (locator[index] != '%')
                {
                    decoded.Append(locator[index++]);
                    continue;
                }

                var octets = new List<byte>();
                while (index < locator.Length && locator[index] == '%')
                {
                    octets.Add(byte.Parse(locator.AsSpan(index + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    index += 3;
                }

                decoded.Append(StrictUtf8.GetString([.. octets]));
            }

            locator = decoded.ToString();
        }
        catch (DecoderFallbackException)
        {
            reason = "percent encoding is invalid";
            return false;
        }

        if (locator.Contains('\\') || locator.StartsWith('/') || locator.Contains('\0') || DrivePathPattern().IsMatch(locator) ||
            locator.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            reason = "decoded locator is absolute or contains a prohibited separator or NUL";
            return false;
        }

        if (locator.Length == 0)
        {
            resolved = sourcePath.Normalize();
            return true;
        }

        var stack = new List<string>(sourcePath.Split('/').SkipLast(1));
        foreach (var segment in locator.Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..")
            {
                if (stack.Count == 0)
                {
                    reason = "resolution escapes the project root";
                    return false;
                }

                stack.RemoveAt(stack.Count - 1);
            }
            else stack.Add(segment);
        }

        resolved = string.Join('/', stack).Normalize();
        return true;
    }

    private static bool IsExternal(string destination) =>
        destination.StartsWith("//", StringComparison.Ordinal) || SchemePattern().IsMatch(destination) &&
        !destination.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && !DrivePathPattern().IsMatch(destination);

    private static bool IdentifierToken(string text, string id)
    {
        for (var offset = 0; offset < text.Length;)
        {
            var index = text.IndexOf(id, offset, StringComparison.Ordinal);
            if (index < 0) return false;
            if ((index == 0 || !char.IsAsciiLetterOrDigit(text[index - 1])) &&
                (index + id.Length == text.Length || !char.IsAsciiLetterOrDigit(text[index + id.Length]))) return true;
            offset = index + id.Length;
        }

        return false;
    }

    private static string Fold(string value) => UnicodeCaseFolding.Fold(value);

    private static bool IsReadException(Exception exception) => exception is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    private static DocumentationValidationResult Result(
        ProjectDiscoveryResult discovery,
        IEnumerable<ValidationDocument> documents,
        IEnumerable<ValidationDiagnostic> diagnostics,
        ValidationToolError? error = null) =>
        new(discovery.SuppliedPath, discovery.ProjectRoot, documents, diagnostics, error);

    private static IEnumerable<ValidationDocument> ToDocuments(IEnumerable<DocumentState> states) => states.Select(state => state.ToDocument());

    private static ValidationDiagnostic Diagnostic(ValidationDiagnosticCode code, string message) => new(code, message);

    private static string[] SplitLines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static readonly string[] RelationshipHeadings = ["Supersedes", "Superseded by", "Amends", "Amended by", "Related decisions"];
    private static readonly int[] DateDigitPositions = [0, 1, 2, 3, 5, 6, 8, 9];

    [GeneratedRegex("^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.CultureInvariant)]
    private static partial Regex ReservedNamePattern();

    [GeneratedRegex("^[ ]{0,3}Documentation contract: llamarc42/v0\\.1[ \\t]*$", RegexOptions.CultureInvariant)]
    private static partial Regex DeclarationPattern();

    [GeneratedRegex("^[ ]{0,3}Documentation contract: .+?[ \\t]*$", RegexOptions.CultureInvariant)]
    private static partial Regex UnsupportedDeclarationPattern();

    [GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}(?:-[a-z0-9]+(?:-[a-z0-9]+)*)?\\.md$", RegexOptions.CultureInvariant)]
    private static partial Regex DailyExactPattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9+.-]*:", RegexOptions.CultureInvariant)]
    private static partial Regex SchemePattern();

    [GeneratedRegex("^[A-Za-z]:[/\\\\]", RegexOptions.CultureInvariant)]
    private static partial Regex DrivePathPattern();

    [GeneratedRegex("^decisions/ADR-[0-9]{3}-[a-z0-9]+(?:-[a-z0-9]+)*\\.md$", RegexOptions.CultureInvariant)]
    private static partial Regex DecisionCandidatePattern();

    private sealed class DocumentState
    {
        internal DocumentState(DocumentInventoryEntry entry, string text)
        {
            Entry = entry;
            Text = text;
            Lines = SplitLines(text);
            Document = Markdown.Parse(text, MarkdownPipeline);
            var masked = text.ToCharArray();
            foreach (var node in Document.Descendants().Where(node => node is HtmlBlock or HtmlInline))
            {
                var fragment = text.Substring(node.Span.Start, node.Span.End - node.Span.Start + 1);
                foreach (Match match in CommentPattern().Matches(fragment))
                    for (var index = node.Span.Start + match.Index; index < node.Span.Start + match.Index + match.Length; index++)
                        if (masked[index] is not ('\r' or '\n')) masked[index] = ' ';
            }
            CommentFreeLines = SplitLines(new string(masked));
            Authority = TypeAuthority(entry.DocumentType);
        }

        private DocumentState(DocumentInventoryEntry entry)
        {
            Entry = entry;
            Lines = [];
            CommentFreeLines = [];
            Document = [];
            Authority = entry.DocumentType == DocumentType.DecisionRecord ||
                entry.DocumentType == DocumentType.CurrentGuidance && entry.Path.StartsWith("specifications/", StringComparison.Ordinal)
                ? AuthorityState.Indeterminate : TypeAuthority(entry.DocumentType);
        }

        internal DocumentInventoryEntry Entry { get; }
        internal string? Text { get; }
        internal string[] Lines { get; }
        internal string[] CommentFreeLines { get; }
        internal MarkdownDocument Document { get; }
        internal AuthorityState Authority { get; set; }
        internal string? LifecycleStatus { get; set; }
        internal bool IdentifierValid { get; set; } = true;

        internal static DocumentState Unreadable(DocumentInventoryEntry entry) => new(entry);
        internal static DocumentState Undecodable(DocumentInventoryEntry entry) => new(entry);

        internal ValidationDocument ToDocument() => new(Entry.SourcePath, Entry.Path, Entry.DocumentType, Authority, LifecycleStatus, Entry.DecisionId);

        internal ValidationDiagnostic Diagnostic(ValidationDiagnosticCode code, string message, int? line = null, string? destination = null) =>
            new(code, message, Entry.Path, Entry.SourcePath, line, destination: destination);

        internal IEnumerable<(string Text, int Line)> Headings(int level, bool atxOnly = false) => Document.Descendants<HeadingBlock>()
            .Where(heading => heading.Level == level && (!atxOnly || !heading.IsSetext))
            .Select(heading => (InlineText(heading.Inline), heading.Line + 1));

        internal IEnumerable<(string Value, int Line)> HeaderFields(string? name)
        {
            var firstH1 = Document.OfType<HeadingBlock>().FirstOrDefault(heading => heading.Level == 1);
            if (firstH1 is null) yield break;
            var end = Document.Descendants<HeadingBlock>().Where(heading => heading.Level >= 2 && heading.Line > firstH1.Line)
                .Select(heading => heading.Line).DefaultIfEmpty(Lines.Length).Min();
            for (var index = firstH1.Line + 1; index < end; index++)
            {
                if (!IsTopLevelParagraphLine(index)) continue;
                var match = HeaderPattern().Match(Lines[index]);
                if (match.Success && (name is null || match.Groups["name"].Value == name))
                    yield return (match.Groups["value"].Value, index + 1);
            }
        }

        internal bool IsTopLevelParagraphLine(int line) => Document.OfType<ParagraphBlock>().Any(block =>
            line >= block.Line && line < block.Line + CountSourceLines(Text!, block.Span.Start, block.Span.End) &&
            !Lines.Skip(block.Line).Take(CountSourceLines(Text!, block.Span.Start, block.Span.End))
                .Any(text => text.Contains('|') && text.Trim().Trim('|').Split('|').All(cell => TableDelimiterPattern().IsMatch(cell.Trim()))));

        internal IEnumerable<Section> Sections(string name)
        {
            var headings = Document.Descendants<HeadingBlock>().Where(heading => heading.Level == 2).ToArray();
            for (var index = 0; index < headings.Length; index++)
            {
                if (InlineText(headings[index].Inline) != name) continue;
                var start = headings[index].Line + 1;
                var end = Document.Descendants<HeadingBlock>().Where(heading => heading.Level <= 2 && heading.Line >= start)
                    .Select(heading => heading.Line).DefaultIfEmpty(Lines.Length).Min();
                var content = CommentFreeLines.Select((text, line) => (Text: text, Line: line + 1)).Skip(start).Take(end - start)
                    .Where(item => !string.IsNullOrWhiteSpace(item.Text)).ToArray();
                var links = Links().Where(link => link.Line > start && link.Line <= end).ToArray();
                yield return new Section(name, headings[index].Line + 1, content, links);
            }
        }

        internal IEnumerable<Link> Links()
        {
            var links = Document.Descendants<LinkInline>().Where(link => !link.IsImage && link.Url is not null)
                .Select(link => (Destination: link.Url!, link.Line))
                .Concat(Document.Descendants<AutolinkInline>().Select(link => (Destination: link.IsEmail ? "mailto:" + link.Url : link.Url, link.Line)))
                .OrderBy(link => link.Line);
            foreach (var (destination, sourceLine) in links)
            {
                var line = sourceLine + 1;
                var heading = Entry.DocumentType == DocumentType.DecisionRecord
                    ? RelationshipHeadings.FirstOrDefault(name => SectionsWithoutLinks(name).Any(section => line > section.Start + 1 && line <= section.End))
                    : null;
                yield return new Link(destination, line, heading);
            }
        }

        // Populated only by the shared local-link checks, never by a second resolver.
        internal List<(string Heading, DocumentState Target)> ValidatedRelationships { get; } = [];

        internal IEnumerable<DocumentState> ResolvedRelationships(string heading) => ValidatedRelationships
            .Where(edge => edge.Heading == heading).GroupBy(edge => edge.Target)
            .Where(group => group.Count() == 1).Select(group => group.Key);

        private IEnumerable<(int Start, int End)> SectionsWithoutLinks(string name)
        {
            var headings = Document.Descendants<HeadingBlock>().Where(heading => heading.Level == 2).ToArray();
            for (var index = 0; index < headings.Length; index++)
                if (InlineText(headings[index].Inline) == name)
                    yield return (headings[index].Line, Document.Descendants<HeadingBlock>()
                        .Where(heading => heading.Level <= 2 && heading.Line > headings[index].Line)
                        .Select(heading => heading.Line).DefaultIfEmpty(Lines.Length).Min());
        }

        private static AuthorityState TypeAuthority(DocumentType type) => type switch
        {
            DocumentType.ProjectDescriptor or DocumentType.CurrentGuidance => AuthorityState.Current,
            DocumentType.HistoricalRecord => AuthorityState.NonCurrent,
            _ => AuthorityState.None,
        };
    }

    private sealed record Section(string Name, int Line, IReadOnlyList<(string Text, int Line)> Content, IReadOnlyList<Link> Links);
    private sealed record Link(string Destination, int Line, string? RelationshipHeading);
    private sealed record PhysicalInventory(IReadOnlyList<string> Files, IReadOnlyList<string> ExcludedBoundaries);

    private static string InlineText(ContainerInline? inline)
    {
        if (inline is null) return string.Empty;
        var builder = new StringBuilder();
        foreach (var item in inline)
        {
            if (item is LiteralInline literal) builder.Append(literal.Content);
            else if (item is CodeInline code) builder.Append(code.Content);
            else if (item is ContainerInline container) builder.Append(InlineText(container));
        }
        return builder.ToString().Trim();
    }

    [GeneratedRegex("^(?<name>[A-Za-z][A-Za-z ]*):[ \\t]*(?<value>.*?)[ \\t]*$", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderPattern();

    [GeneratedRegex("<!--.*?(?:-->|$)", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex CommentPattern();

    [GeneratedRegex("^:?-+:?$", RegexOptions.CultureInvariant)]
    private static partial Regex TableDelimiterPattern();
}
