namespace Llamarc42.Core.Discovery;

/// <summary>
/// A discovered Markdown document with its source locator and stable identity.
/// </summary>
/// <param name="SourcePath">
/// Project-relative path using <c>/</c>, preserving the filesystem representation.
/// </param>
/// <param name="Path">
/// Stable project-relative identity using <c>/</c> and Unicode NFC normalization.
/// </param>
/// <param name="DocumentType">The structural type derived from <paramref name="Path"/>.</param>
/// <param name="DecisionId">
/// The <c>ADR-NNN</c> identity for a structurally conforming decision-record path; otherwise <see langword="null"/>.
/// </param>
public sealed record DocumentInventoryEntry(
    string SourcePath,
    string Path,
    DocumentType DocumentType,
    string? DecisionId);
