using UProjectHub.Core.Models;

namespace UProjectHub.Core.Filtering;

public sealed record ProjectFilter(
    string? Engine = null,
    ProjectType? ProjectType = null,
    bool FavoritesOnly = false);
