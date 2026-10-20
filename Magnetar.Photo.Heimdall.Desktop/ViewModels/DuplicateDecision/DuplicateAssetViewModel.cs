using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace Magnetar.Photo.Heimdall.Desktop.ViewModels.DuplicateDecision;

public sealed class DuplicateAssetViewModel : INotifyPropertyChanged
{
    private bool _isKeeper;

    public DuplicateAssetViewModel(string filePath, long sizeInBytes, string algorithm, string version, string hash)
    {
        FilePath = filePath;
        SizeInBytes = sizeInBytes;
        Algorithm = algorithm;
        Version = version;
        Hash = hash;
    }

    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);
    public string DirectoryPath => Path.GetDirectoryName(FilePath) ?? string.Empty;
    public long SizeInBytes { get; }
    public string Algorithm { get; }
    public string Version { get; }
    public string Hash { get; }

    public bool IsKeeper
    {
        get => _isKeeper;
        set
        {
            if (_isKeeper != value)
            {
                _isKeeper = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
