namespace Llamarc42.Core.Validation;

/// <summary>A stable tool failure that prevented complete conformance evaluation.</summary>
/// <param name="Code">Stable programmatic meaning.</param>
/// <param name="Message">Deterministic, actionable failure description.</param>
public sealed record ValidationToolError(
    ValidationToolErrorCode Code,
    string Message);
