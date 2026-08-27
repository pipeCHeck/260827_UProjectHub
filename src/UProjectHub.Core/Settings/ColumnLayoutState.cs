namespace UProjectHub.Core.Settings;

public sealed record ColumnLayoutState(
    string ColumnId,
    bool IsVisible = true,
    double? Width = null);
