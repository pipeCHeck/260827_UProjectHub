using UProjectHub.Core.Models;

namespace UProjectHub.Core.Settings;

public sealed record VisibleFilterState(
    string? Engine = null,
    ProjectType? ProjectType = null,
    bool FavoritesOnly = false,
    string? Tag = null);
