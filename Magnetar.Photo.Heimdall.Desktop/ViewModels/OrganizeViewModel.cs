using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Magnetar.Photo.Heimdall.Desktop.ViewModels;

public sealed class OrganizeViewModel : INotifyPropertyChanged
{
    private string _folderPattern = string.Empty;
    private string _filePattern = string.Empty;
    private int _missingMetadataStrategyIndex;
    private int _collisionStrategyIndex;

    public OrganizeViewModel()
    {
        PreviewItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasPreviewData));
    }

    public string FolderPattern
    {
        get => _folderPattern;
        set
        {
            if (_folderPattern != value)
            {
                _folderPattern = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsFolderPatternValid));
            }
        }
    }

    public string FilePattern
    {
        get => _filePattern;
        set
        {
            if (_filePattern != value)
            {
                _filePattern = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsFilePatternValid));
            }
        }
    }

    public int MissingMetadataStrategyIndex
    {
        get => _missingMetadataStrategyIndex;
        set
        {
            if (_missingMetadataStrategyIndex != value)
            {
                _missingMetadataStrategyIndex = value;
                OnPropertyChanged();
            }
        }
    }

    public int CollisionStrategyIndex
    {
        get => _collisionStrategyIndex;
        set
        {
            if (_collisionStrategyIndex != value)
            {
                _collisionStrategyIndex = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsFolderPatternValid => !string.IsNullOrWhiteSpace(FolderPattern);
    public bool IsFilePatternValid => !string.IsNullOrWhiteSpace(FilePattern);

    // Counts
    public int NoConflictCount { get; }
    public int RequiresReviewCount { get; }
    public int AlreadyOrganizedCount { get; }

    // Preview state
    public bool HasPreviewData => PreviewItems.Count > 0;

    public ObservableCollection<object> PreviewItems { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
