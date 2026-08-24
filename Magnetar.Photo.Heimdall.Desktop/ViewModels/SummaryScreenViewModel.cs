using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Magnetar.Photo.Heimdall.Desktop.ViewModels;

/// <summary>
/// Read-only dashboard projection of the real library-onboarding state.
/// Metrics without an implemented catalog query are deliberately exposed as
/// presentation data, so the UI never presents a sample value as a fact.
/// </summary>
public sealed class SummaryScreenViewModel : INotifyPropertyChanged
{
    private readonly LibraryScreenViewModel _libraryScreen;
    private readonly string _presentationDataLabel = "Datos de presentación — backend pendiente";

    public SummaryScreenViewModel(LibraryScreenViewModel libraryScreen)
    {
        _libraryScreen = libraryScreen;
        _libraryScreen.PropertyChanged += OnLibraryScreenPropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int CataloguedAssetCount => _libraryScreen.CataloguedCount;
    public string CurrentLibraryName => _libraryScreen.LastScannedLibraryName ?? "No library scanned";
    public string CatalogStatus => _libraryScreen.StatusMessage;
    public bool IsScanning => _libraryScreen.IsBusy;
    public bool HasCataloguedAssets => _libraryScreen.HasCataloguedCount;

    /// <summary>Explicitly labels cards whose backend capability is not built yet.</summary>
    public string PresentationDataLabel => _presentationDataLabel;

    private void OnLibraryScreenPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        switch (eventArgs.PropertyName)
        {
            case nameof(LibraryScreenViewModel.CataloguedCount):
            case nameof(LibraryScreenViewModel.HasCataloguedCount):
                OnPropertyChanged(nameof(CataloguedAssetCount));
                OnPropertyChanged(nameof(HasCataloguedAssets));
                break;
            case nameof(LibraryScreenViewModel.LastScannedLibraryName):
                OnPropertyChanged(nameof(CurrentLibraryName));
                break;
            case nameof(LibraryScreenViewModel.StatusMessage):
                OnPropertyChanged(nameof(CatalogStatus));
                break;
            case nameof(LibraryScreenViewModel.IsBusy):
                OnPropertyChanged(nameof(IsScanning));
                break;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
