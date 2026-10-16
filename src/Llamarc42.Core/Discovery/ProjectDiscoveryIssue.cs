namespace Llamarc42.Core.Discovery;

/// <summary>A deterministic, actionable discovery condition.</summary>
/// <param name="Code">Stable programmatic meaning.</param>
/// <param name="Path">Safely reportable absolute path associated with the condition.</param>
/// <param name="Message">Human-readable explanation and retry guidance.</param>
public sealed record ProjectDiscoveryIssue(
    ProjectDiscoveryIssueCode Code,
    string Path,
    string Message);
