using Llamarc42.Core.Discovery;

namespace Llamarc42.Core.Validation;

/// <summary>A classified contract-document inventory entry with derived authority.</summary>
/// <param name="SourcePath">Original project-relative source locator.</param>
/// <param name="Path">NFC-normalized project-relative identity.</param>
/// <param name="DocumentType">Structurally derived document type.</param>
/// <param name="AuthorityState">Authority derived under the contract precedence.</param>
/// <param name="LifecycleStatus">Recognized explicit lifecycle status, when present.</param>
/// <param name="DecisionId">Structurally identifiable <c>ADR-NNN</c>, when present.</param>
public sealed record ValidationDocument(
    string SourcePath,
    string Path,
    DocumentType DocumentType,
    AuthorityState AuthorityState,
    string? LifecycleStatus,
    string? DecisionId);
