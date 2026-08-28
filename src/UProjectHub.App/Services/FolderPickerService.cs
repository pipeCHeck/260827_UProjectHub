using Microsoft.Win32;

namespace UProjectHub.App.Services;

public sealed class FolderPickerService : IFolderPickerService
{
    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
