using UProjectHub.Core.Models;

namespace UProjectHub.Core.Discovery;

public enum ProjectDiscoveryIssueKind
{
    RootScan,
    MetadataLoad,
}

public sealed record ProjectDiscoveryIssue(
    string Path,
    ProjectDiscoveryIssueKind Kind,
    string Message);

public sealed record ProjectMetadataLoadResult(
    UnrealProject Project,
    ProjectDiscoveryIssue? Issue);

public sealed record ProjectDiscoveryResult(
    IReadOnlyList<UnrealProject> Projects,
    IReadOnlyList<ProjectDiscoveryIssue> Issues);
