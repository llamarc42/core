using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Llamarc42.Core.Discovery;

/// <summary>
/// Discovers a Documentation Contract v0.1 project and builds its structural document inventory.
/// </summary>
public static partial class ProjectDiscovery
{
    private const string RootMarkerName = "project.md";

    private static readonly string[] CurrentGuidanceDirectories =
    [
        "architecture/",
        "specifications/",
        "domain/",
        "guides/",
        "quality/",
    ];

    /// <summary>
    /// Searches upward from <paramref name="startPath"/> for the nearest exact, non-link regular
    /// <c>project.md</c>, then creates the contract document inventory without following links or
    /// traversing nested projects.
    /// </summary>
    public static ProjectDiscoveryResult Discover(string startPath) =>
        Discover(startPath, FileSystemEntryInspector.Inspect);

    internal static ProjectDiscoveryResult Discover(
        string startPath,
        Func<string, FileAttributes, FileSystemEntryKind> inspectEntry)
    {
        var suppliedPath = startPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return Failure(
                suppliedPath,
                normalizedStartPath: null,
                ProjectDiscoveryIssueCode.StartPathInvalid,
                suppliedPath,
                "The supplied start path is empty. Supply an existing file or directory and retry.");
        }

        string normalizedStartPath;
        try
        {
            normalizedStartPath = Path.GetFullPath(startPath);
        }
        catch (Exception exception) when (IsPathInputException(exception))
        {
            return Failure(
                suppliedPath,
                normalizedStartPath: null,
                ProjectDiscoveryIssueCode.StartPathInvalid,
                suppliedPath,
                $"The supplied start path cannot be normalized ({exception.GetType().Name}). Supply a valid existing path and retry.");
        }

        FileAttributes startAttributes;
        try
        {
            startAttributes = File.GetAttributes(normalizedStartPath);
        }
        catch (Exception exception) when (IsNotFoundException(exception))
        {
            return Failure(
                suppliedPath,
                normalizedStartPath,
                ProjectDiscoveryIssueCode.StartPathNotFound,
                normalizedStartPath,
                "The supplied start path does not exist. Supply an existing file or directory and retry.");
        }
        catch (Exception exception) when (IsFileSystemInspectionException(exception))
        {
            return Failure(
                suppliedPath,
                normalizedStartPath,
                ProjectDiscoveryIssueCode.StartPathInaccessible,
                normalizedStartPath,
                $"The supplied start path cannot be inspected ({exception.GetType().Name}). Correct access to the path and retry.");
        }

        string? searchDirectory;
        if ((startAttributes & FileAttributes.Directory) != 0)
        {
            searchDirectory = normalizedStartPath;
        }
        else
        {
            searchDirectory = Path.GetDirectoryName(normalizedStartPath);
            if (searchDirectory is null)
            {
                return Failure(
                    suppliedPath,
                    normalizedStartPath,
                    ProjectDiscoveryIssueCode.ParentInspectionFailed,
                    normalizedStartPath,
                    "The parent directory of the supplied file cannot be determined. Supply a path with an inspectable parent and retry.");
            }
        }

        var issues = new List<ProjectDiscoveryIssue>();

        while (searchDirectory is not null)
        {
            List<string> entries;
            try
            {
                entries = [.. Directory.EnumerateFileSystemEntries(searchDirectory)];
            }
            catch (Exception exception) when (IsFileSystemInspectionException(exception))
            {
                issues.Add(new ProjectDiscoveryIssue(
                    ProjectDiscoveryIssueCode.RootInspectionFailed,
                    searchDirectory,
                    $"The directory cannot be enumerated while searching for {RootMarkerName} ({exception.GetType().Name}). Correct access and retry; no ancestor was selected through this condition."));

                return Result(
                    suppliedPath,
                    normalizedStartPath,
                    ProjectDiscoveryOutcome.Error,
                    projectRoot: null,
                    documents: [],
                    issues);
            }

            var markers = entries
                .Where(path => string.Equals(Path.GetFileName(path), RootMarkerName, StringComparison.Ordinal))
                .OrderBy(path => path, UnicodeCodePointComparer.Instance)
                .ToArray();

            if (markers.Length > 1)
            {
                issues.Add(new ProjectDiscoveryIssue(
                    ProjectDiscoveryIssueCode.RootMarkerAmbiguous,
                    searchDirectory,
                    $"More than one exact {RootMarkerName} entry was enumerated. Remove the ambiguous entries and retry."));

                return Result(
                    suppliedPath,
                    normalizedStartPath,
                    ProjectDiscoveryOutcome.Error,
                    projectRoot: null,
                    documents: [],
                    issues);
            }

            if (markers.Length == 1)
            {
                var markerPath = markers[0];
                FileAttributes markerAttributes;
                FileSystemEntryKind markerKind;

                try
                {
                    markerAttributes = File.GetAttributes(markerPath);
                    markerKind = inspectEntry(markerPath, markerAttributes);
                }
                catch (Exception exception) when (IsFileSystemInspectionException(exception))
                {
                    issues.Add(new ProjectDiscoveryIssue(
                        ProjectDiscoveryIssueCode.RootInspectionFailed,
                        markerPath,
                        $"The {RootMarkerName} entry kind cannot be inspected ({exception.GetType().Name}). Correct access and retry; no ancestor was selected through this condition."));

                    return Result(
                        suppliedPath,
                        normalizedStartPath,
                        ProjectDiscoveryOutcome.Error,
                        projectRoot: null,
                        documents: [],
                        issues);
                }

                if (markerKind == FileSystemEntryKind.Link)
                {
                    issues.Add(new ProjectDiscoveryIssue(
                        ProjectDiscoveryIssueCode.RootMarkerLink,
                        markerPath,
                        $"The exact {RootMarkerName} entry is a link or reparse point and cannot mark a project root. Discovery will continue toward the filesystem root."));
                }
                else if (markerKind != FileSystemEntryKind.RegularFile)
                {
                    issues.Add(new ProjectDiscoveryIssue(
                        ProjectDiscoveryIssueCode.RootMarkerNotRegular,
                        markerPath,
                        $"The exact {RootMarkerName} entry is not a regular file and cannot mark a project root. Discovery will continue toward the filesystem root."));
                }
                else
                {
                    return BuildInventory(
                        suppliedPath,
                        normalizedStartPath,
                        searchDirectory,
                        issues,
                        inspectEntry);
                }
            }

            try
            {
                searchDirectory = Directory.GetParent(searchDirectory)?.FullName;
            }
            catch (Exception exception) when (IsFileSystemInspectionException(exception) || exception is ArgumentException)
            {
                issues.Add(new ProjectDiscoveryIssue(
                    ProjectDiscoveryIssueCode.ParentInspectionFailed,
                    searchDirectory ?? normalizedStartPath,
                    $"The parent directory cannot be determined ({exception.GetType().Name}). Correct the path or access condition and retry."));

                return Result(
                    suppliedPath,
                    normalizedStartPath,
                    ProjectDiscoveryOutcome.Error,
                    projectRoot: null,
                    documents: [],
                    issues);
            }
        }

        return Result(
            suppliedPath,
            normalizedStartPath,
            ProjectDiscoveryOutcome.NotFound,
            projectRoot: null,
            documents: [],
            issues);
    }

    private static ProjectDiscoveryResult BuildInventory(
        string suppliedPath,
        string normalizedStartPath,
        string projectRoot,
        List<ProjectDiscoveryIssue> issues,
        Func<string, FileAttributes, FileSystemEntryKind> inspectEntry)
    {
        var documents = new List<DocumentInventoryEntry>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(projectRoot);

        while (pendingDirectories.Count > 0)
        {
            var directory = pendingDirectories.Pop();
            List<string> entries;

            try
            {
                entries =
                [
                    .. Directory.EnumerateFileSystemEntries(directory)
                        .OrderBy(path => Path.GetFileName(path), UnicodeCodePointComparer.Instance),
                ];
            }
            catch (Exception exception) when (IsFileSystemInspectionException(exception))
            {
                issues.Add(new ProjectDiscoveryIssue(
                    ProjectDiscoveryIssueCode.InventoryEnumerationFailed,
                    directory,
                    $"The directory cannot be enumerated while building the document inventory ({exception.GetType().Name}). Correct access and retry."));

                return Result(
                    suppliedPath,
                    normalizedStartPath,
                    ProjectDiscoveryOutcome.Error,
                    projectRoot,
                    OrderDocuments(documents),
                    issues);
            }

            var inspectionFailed = false;
            if (!string.Equals(directory, projectRoot, StringComparison.Ordinal)
                && ContainsValidRootMarker(entries, issues, inspectEntry, out inspectionFailed))
            {
                continue;
            }

            if (inspectionFailed)
            {
                return Result(
                    suppliedPath,
                    normalizedStartPath,
                    ProjectDiscoveryOutcome.Error,
                    projectRoot,
                    OrderDocuments(documents),
                    issues);
            }

            foreach (var entry in entries)
            {
                FileAttributes attributes;
                FileSystemEntryKind entryKind;

                try
                {
                    attributes = File.GetAttributes(entry);
                    entryKind = inspectEntry(entry, attributes);
                }
                catch (Exception exception) when (IsFileSystemInspectionException(exception))
                {
                    issues.Add(new ProjectDiscoveryIssue(
                        ProjectDiscoveryIssueCode.EntryInspectionFailed,
                        entry,
                        $"The filesystem entry kind cannot be inspected while building the inventory ({exception.GetType().Name}). Correct access and retry."));

                    return Result(
                        suppliedPath,
                        normalizedStartPath,
                        ProjectDiscoveryOutcome.Error,
                        projectRoot,
                        OrderDocuments(documents),
                        issues);
                }

                if (entryKind == FileSystemEntryKind.Link)
                {
                    continue;
                }

                if (entryKind == FileSystemEntryKind.Directory)
                {
                    pendingDirectories.Push(entry);
                    continue;
                }

                if (entryKind != FileSystemEntryKind.RegularFile)
                {
                    continue;
                }

                if (!string.Equals(Path.GetExtension(entry), ".md", StringComparison.Ordinal))
                {
                    continue;
                }

                var sourcePath = Path.GetRelativePath(projectRoot, entry)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');

                if (Path.IsPathRooted(sourcePath)
                    || sourcePath.Equals("..", StringComparison.Ordinal)
                    || sourcePath.StartsWith("../", StringComparison.Ordinal))
                {
                    issues.Add(new ProjectDiscoveryIssue(
                        ProjectDiscoveryIssueCode.PathBoundaryViolation,
                        entry,
                        "The discovered document does not normalize beneath the selected project root. Select a direct project path and retry."));

                    return Result(
                        suppliedPath,
                        normalizedStartPath,
                        ProjectDiscoveryOutcome.Error,
                        projectRoot,
                        OrderDocuments(documents),
                        issues);
                }

                var normalizedPath = sourcePath.Normalize(NormalizationForm.FormC);
                var (documentType, decisionId) = Classify(normalizedPath);
                documents.Add(new DocumentInventoryEntry(sourcePath, normalizedPath, documentType, decisionId));
            }
        }

        return Result(
            suppliedPath,
            normalizedStartPath,
            ProjectDiscoveryOutcome.Discovered,
            projectRoot,
            OrderDocuments(documents),
            issues);
    }

    private static bool ContainsValidRootMarker(
        IEnumerable<string> entries,
        List<ProjectDiscoveryIssue> issues,
        Func<string, FileAttributes, FileSystemEntryKind> inspectEntry,
        out bool inspectionFailed)
    {
        inspectionFailed = false;
        var markers = entries
            .Where(path => string.Equals(Path.GetFileName(path), RootMarkerName, StringComparison.Ordinal))
            .ToArray();

        if (markers.Length != 1)
        {
            return false;
        }

        try
        {
            var attributes = File.GetAttributes(markers[0]);
            return inspectEntry(markers[0], attributes)
                == FileSystemEntryKind.RegularFile;
        }
        catch (Exception exception) when (IsFileSystemInspectionException(exception))
        {
            issues.Add(new ProjectDiscoveryIssue(
                ProjectDiscoveryIssueCode.EntryInspectionFailed,
                markers[0],
                $"A possible nested {RootMarkerName} entry cannot be inspected ({exception.GetType().Name}). Correct access and retry."));
            inspectionFailed = true;
            return false;
        }
    }

    private static (DocumentType Type, string? DecisionId) Classify(string path)
    {
        if (string.Equals(path, RootMarkerName, StringComparison.Ordinal))
        {
            return (DocumentType.ProjectDescriptor, null);
        }

        var decisionMatch = DecisionPathPattern().Match(path);
        if (decisionMatch.Success)
        {
            return (DocumentType.DecisionRecord, $"ADR-{decisionMatch.Groups[1].Value}");
        }

        if (string.Equals(path, "context.md", StringComparison.Ordinal)
            || string.Equals(path, "constraints.md", StringComparison.Ordinal)
            || CurrentGuidanceDirectories.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return (DocumentType.CurrentGuidance, null);
        }

        if (path.StartsWith("history/", StringComparison.Ordinal))
        {
            return (DocumentType.HistoricalRecord, null);
        }

        if (string.Equals(path, "AGENTS.md", StringComparison.Ordinal)
            || string.Equals(path, "README.md", StringComparison.Ordinal))
        {
            return (DocumentType.Navigation, null);
        }

        return (DocumentType.Supplemental, null);
    }

    private static DocumentInventoryEntry[] OrderDocuments(
        IEnumerable<DocumentInventoryEntry> documents) =>
        [.. documents.OrderBy(document => document, DocumentInventoryEntryComparer.Instance)];

    private static ProjectDiscoveryResult Failure(
        string suppliedPath,
        string? normalizedStartPath,
        ProjectDiscoveryIssueCode code,
        string path,
        string message) =>
        Result(
            suppliedPath,
            normalizedStartPath,
            ProjectDiscoveryOutcome.Error,
            projectRoot: null,
            documents: [],
            issues: [new ProjectDiscoveryIssue(code, path, message)]);

    private static ProjectDiscoveryResult Result(
        string suppliedPath,
        string? normalizedStartPath,
        ProjectDiscoveryOutcome outcome,
        string? projectRoot,
        IEnumerable<DocumentInventoryEntry> documents,
        IEnumerable<ProjectDiscoveryIssue> issues) =>
        new(suppliedPath, normalizedStartPath, outcome, projectRoot, documents, issues);

    private static bool IsPathInputException(Exception exception) =>
        exception is ArgumentException or NotSupportedException or PathTooLongException;

    private static bool IsNotFoundException(Exception exception) =>
        exception is FileNotFoundException or DirectoryNotFoundException;

    private static bool IsFileSystemInspectionException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    [GeneratedRegex(@"^decisions/ADR-([0-9]{3})-[a-z0-9]+(?:-[a-z0-9]+)*\.md$", RegexOptions.CultureInvariant)]
    private static partial Regex DecisionPathPattern();

    private sealed class DocumentInventoryEntryComparer : IComparer<DocumentInventoryEntry>
    {
        public static DocumentInventoryEntryComparer Instance { get; } = new();

        public int Compare(DocumentInventoryEntry? x, DocumentInventoryEntry? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var typeComparison = x.DocumentType.CompareTo(y.DocumentType);
            if (typeComparison != 0)
            {
                return typeComparison;
            }

            if (x.DocumentType == DocumentType.DecisionRecord)
            {
                var xNumber = int.Parse(x.DecisionId![4..], CultureInfo.InvariantCulture);
                var yNumber = int.Parse(y.DecisionId![4..], CultureInfo.InvariantCulture);
                var numberComparison = xNumber.CompareTo(yNumber);
                if (numberComparison != 0)
                {
                    return numberComparison;
                }
            }

            var pathComparison = UnicodeCodePointComparer.Instance.Compare(x.Path, y.Path);
            return pathComparison != 0
                ? pathComparison
                : UnicodeCodePointComparer.Instance.Compare(x.SourcePath, y.SourcePath);
        }
    }
}
