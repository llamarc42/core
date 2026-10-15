namespace Llamarc42.Core.Discovery;

/// <summary>
/// Stable programmatic meanings for conditions observed while discovering a project.
/// These are Core discovery meanings, not validation diagnostic codes.
/// </summary>
public enum ProjectDiscoveryIssueCode
{
    StartPathInvalid,
    StartPathNotFound,
    StartPathInaccessible,
    RootMarkerAmbiguous,
    RootMarkerLink,
    RootMarkerNotRegular,
    RootInspectionFailed,
    ParentInspectionFailed,
    InventoryEnumerationFailed,
    EntryInspectionFailed,
    PathBoundaryViolation,
}
