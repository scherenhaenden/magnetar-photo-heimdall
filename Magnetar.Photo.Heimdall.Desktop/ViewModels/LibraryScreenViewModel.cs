using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Magnetar.Photo.Heimdall.PresentationLogic;

namespace Magnetar.Photo.Heimdall.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Libraries onboarding screen.
/// Has no dependency on Avalonia UI types: all platform interaction is
/// injected via delegates (folderPicker) from the composition root.
/// </summary>
public sealed class LibraryScreenViewModel : INotifyPropertyChanged
{
    private readonly LibraryOnboardingFacade _facade;
    private string _displayName = "My photo library";
    private string _rootPath = string.Empty;
    private string _statusMessage = "Add a library folder to start cataloguing your photos.";
    private string? _errorMessage;
    private int _cataloguedCount;
    private bool _isBusy;

    /// <param name="facade">Presentation-layer facade; the only dependency this VM knows about.</param>
    /// <param name="folderPicker">
    ///   Optional async delegate that returns an absolute folder path chosen by the user,
    ///   or <see langword="null"/> when the dialog is cancelled.
    ///   Pass <see langword="null"/> (default) to disable the folder-picker button.
    /// </param>
    public LibraryScreenViewModel(
        LibraryOnboardingFacade facade,
        Func<Task<string?>>? folderPicker = null)
    {
        _facade = facade;

        AddAndScanCommand = new AsyncCommand(ExecuteAddAndScanAsync, CanAddAndScan);

        ChooseFolderCommand = folderPicker is not null
            ? new AsyncCommand(async () =>
            {
                var picked = await folderPicker();
                if (picked is not null)
                    RootPath = picked;
            }, () => !IsBusy)
            : new AsyncCommand(() => Task.CompletedTask, () => false); // disabled — no picker provided
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ── Commands ──────────────────────────────────────────────────────────
    public ICommand AddAndScanCommand { get; }
    public ICommand ChooseFolderCommand { get; }

    // ── Input properties ─────────────────────────────────────────────────

    /// <summary>Human-readable name the user assigns to this library.</summary>
    public string DisplayName
    {
        get => _displayName;
        set { if (SetField(ref _displayName, value)) RaiseCanExecuteChanged(); }
    }

    /// <summary>Absolute path to the root folder of the library.</summary>
    public string RootPath
    {
        get => _rootPath;
        set { if (SetField(ref _rootPath, value)) RaiseCanExecuteChanged(); }
    }

    // ── Output / status properties ────────────────────────────────────────

    /// <summary>General status text shown below the form (scanning progress, completion notice, etc.).</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    /// <summary>Human-readable error message from the last failed operation; <see langword="null"/> when no error.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    /// <summary><see langword="true"/> when the last operation produced an error.</summary>
    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    /// <summary>Number of media assets catalogued in the most recent successful scan.</summary>
    public int CataloguedCount
    {
        get => _cataloguedCount;
        private set
        {
            if (SetField(ref _cataloguedCount, value))
                OnPropertyChanged(nameof(HasCataloguedCount));
        }
    }

    /// <summary><see langword="true"/> after a successful scan that found at least one asset.</summary>
    public bool HasCataloguedCount => _cataloguedCount > 0;

    /// <summary>
    ///   <see langword="true"/> while an operation is running.
    ///   Also used to disable input controls during scanning.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsInputEnabled));
                RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Convenience inverse of <see cref="IsBusy"/> for binding <c>IsEnabled</c> on input controls.</summary>
    public bool IsInputEnabled => !_isBusy;

    // ── Private implementation ────────────────────────────────────────────

    private bool CanAddAndScan() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(DisplayName) &&
        !string.IsNullOrWhiteSpace(RootPath);

    private async Task ExecuteAddAndScanAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        CataloguedCount = 0;
        StatusMessage = "Adding library and scanning media…";
        try
        {
            var result = await _facade.AddAndScanAsync(DisplayName.Trim(), RootPath.Trim());
            CataloguedCount = result.Scan.CataloguedAssetCount;
            StatusMessage = $"{result.Library.DisplayName}: catalogued {result.Scan.CataloguedAssetCount} media assets.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "Scan failed — see error details below.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void RaiseCanExecuteChanged()
    {
        ((AsyncCommand)AddAndScanCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)ChooseFolderCommand).RaiseCanExecuteChanged();
    }

    // ── Inner ICommand implementation ─────────────────────────────────────

    private sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => canExecute();
        public async void Execute(object? parameter) => await execute();
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
