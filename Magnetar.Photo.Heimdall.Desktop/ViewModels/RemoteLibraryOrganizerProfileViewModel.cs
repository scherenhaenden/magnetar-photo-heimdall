using System.ComponentModel;
using System.Runtime.CompilerServices;
using Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

namespace Magnetar.Photo.Heimdall.Desktop.ViewModels;

/// <summary>Per-NAS-library organization preferences shown in the destination editor.</summary>
public sealed class RemoteLibraryOrganizerProfileViewModel(AgentRootRemoteContractsModel root) : INotifyPropertyChanged
{
    private string _folderPattern = "{camera}/{year}/{month}";
    private string _missingDateFolder = "nodate";
    private string _missingCameraFolder = "unknown";
    private string _filenameTemplate = "{filename}";

    public event PropertyChangedEventHandler? PropertyChanged;
    public string RootId => root.Id;
    public string DisplayName => root.DisplayName;
    public string FolderPattern { get => _folderPattern; set => Set(ref _folderPattern, value); }
    public string MissingDateFolder { get => _missingDateFolder; set => Set(ref _missingDateFolder, value); }
    public string MissingCameraFolder { get => _missingCameraFolder; set => Set(ref _missingCameraFolder, value); }
    public string FilenameTemplate { get => _filenameTemplate; set => Set(ref _filenameTemplate, value); }
    public string CollisionPolicy => "No sobrescribir: conservar el original y registrar conflicto";

    private void Set(ref string field, string? value, [CallerMemberName] string? propertyName = null)
    {
        var normalized = value ?? string.Empty;
        if (field == normalized) return;
        field = normalized;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
