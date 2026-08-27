using UProjectHub.Core.Paths;

namespace UProjectHub.Core.Discovery;

public sealed record ProjectCandidate(ProjectPath ProjectFilePath);

public sealed record ProjectRootScanResult(
    IReadOnlyList<ProjectCandidate> Candidates,
    IReadOnlyList<ProjectDiscoveryIssue> Issues);
