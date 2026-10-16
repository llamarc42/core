namespace Llamarc42.Core.Discovery;

/// <summary>
/// The structural document types defined by Documentation Contract v0.1.
/// Numeric values preserve the contract's inventory group order.
/// </summary>
public enum DocumentType
{
    ProjectDescriptor = 0,
    DecisionRecord = 1,
    CurrentGuidance = 2,
    HistoricalRecord = 3,
    Navigation = 4,
    Supplemental = 5,
}
