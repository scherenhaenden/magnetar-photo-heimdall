using System.Collections.ObjectModel;
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
    private bool _isEmpty = true;
    private bool _isLoaded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PlanOperationViewModel> Operations { get; } = new();

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

    public void SetLoading()
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = null;
        IsEmpty = false;
        IsLoaded = false;
        Operations.Clear();
    }

    public void SetError(string message)
    {
        IsLoading = false;
        HasError = true;
        ErrorMessage = message;
        IsEmpty = false;
        IsLoaded = false;
        Operations.Clear();
    }

    public void SetPlan(IEnumerable<PlanOperationViewModel> operations)
    {
        IsLoading = false;
        HasError = false;
        ErrorMessage = null;

        Operations.Clear();
        foreach (var op in operations)
        {
            Operations.Add(op);
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
