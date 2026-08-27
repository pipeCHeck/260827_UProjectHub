using UProjectHub.Core.Paths;

namespace UProjectHub.Core.Settings;

public sealed record ProjectUserState(
    ProjectPath ProjectPath,
    bool IsFavorite = false,
    DateTimeOffset? LastLaunched = null);
