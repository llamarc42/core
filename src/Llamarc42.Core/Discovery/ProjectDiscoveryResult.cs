namespace Llamarc42.Core.Discovery;

/// <summary>The complete result available from a project discovery attempt.</summary>
public sealed class ProjectDiscoveryResult
{
    internal ProjectDiscoveryResult(
        string suppliedPath,
        string? normalizedStartPath,
        ProjectDiscoveryOutcome outcome,
        string? projectRoot,
        IEnumerable<DocumentInventoryEntry> documents,
        IEnumerable<ProjectDiscoveryIssue> issues)
    {
        SuppliedPath = suppliedPath;
        NormalizedStartPath = normalizedStartPath;
        Outcome = outcome;
        ProjectRoot = projectRoot;
        Documents = Array.AsReadOnly(documents.ToArray());
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    /// <summary>The caller-supplied path, preserved without normalization.</summary>
    public string SuppliedPath { get; }

    /// <summary>The absolute lexical normalization of the supplied path, when available.</summary>
    public string? NormalizedStartPath { get; }

    /// <summary>The aggregate discovery outcome.</summary>
    public ProjectDiscoveryOutcome Outcome { get; }

    /// <summary>The normalized absolute discovered project root, when established.</summary>
    public string? ProjectRoot { get; }

    /// <summary>The stable ordered document inventory available before any failure.</summary>
    public IReadOnlyList<DocumentInventoryEntry> Documents { get; }

    /// <summary>Ordered conditions observed during discovery.</summary>
    public IReadOnlyList<ProjectDiscoveryIssue> Issues { get; }
}
