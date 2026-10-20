using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Magnetar.Photo.Heimdall.Desktop.ViewModels.PlanReview;

public sealed class PlanOperationViewModel
{
    public string Source { get; }
    public string Destination { get; }
    public string Action { get; }
    public string? Warning { get; }

    public PlanOperationViewModel(string source, string destination, string action, string? warning = null)
    {
        Source = source;
        Destination = destination;
        Action = action;
        Warning = warning;
    }
}

public sealed class ReviewPlanViewModel : INotifyPropertyChanged
{
    private bool _isLoading;
    private bool _hasError;
    private string? _errorMessage;
    private bool _isEmpty;
    private bool _isLoaded;
    private bool _isUnloaded = true;
    private bool _hasPlan;
    private readonly ObservableCollection<PlanOperationViewModel> _operations = new();
    private readonly ReadOnlyObservableCollection<PlanOperationViewModel> _readOnlyOperations;

    public ReviewPlanViewModel()
    {
        _readOnlyOperations = new ReadOnlyObservableCollection<PlanOperationViewModel>(_operations);
        _operations.CollectionChanged += OnOperationsChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReadOnlyObservableCollection<PlanOperationViewModel> Operations => _readOnlyOperations;

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetProperty(ref _isEmpty, value);
    }

    public bool IsLoaded
    {
        get => _isLoaded;
        private set => SetProperty(ref _isLoaded, value);
    }

    /// <summary>True until a plan, loading state, or error has been supplied.</summary>
    public bool IsUnloaded
    {
        get => _isUnloaded;
        private set => SetProperty(ref _isUnloaded, value);
    }

    public void SetLoading()
    {
        _hasPlan = false;
        IsLoading = true;
        HasError = false;
        ErrorMessage = null;
        IsEmpty = false;
        IsLoaded = false;
        IsUnloaded = false;
        _operations.Clear();
    }

    public void SetError(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _hasPlan = false;
        IsLoading = false;
        HasError = true;
        ErrorMessage = message;
        IsEmpty = false;
        IsLoaded = false;
        IsUnloaded = false;
        _operations.Clear();
    }

    public void SetPlan(IEnumerable<PlanOperationViewModel> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        // Enumerate before mutating the current view state. A failing or lazy
        // source must not leave a partially replaced preview behind.
        var materializedOperations = operations.ToArray();

        _hasPlan = false;
        IsLoading = false;
        HasError = false;
        ErrorMessage = null;
        _operations.Clear();
        foreach (var operation in materializedOperations)
        {
            _operations.Add(operation);
        }

        _hasPlan = true;
        IsUnloaded = false;
        IsEmpty = Operations.Count == 0;
        IsLoaded = !IsEmpty;
    }

    private void OnOperationsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (!_hasPlan)
        {
            return;
        }

        IsEmpty = Operations.Count == 0;
        IsLoaded = !IsEmpty;
    }

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
