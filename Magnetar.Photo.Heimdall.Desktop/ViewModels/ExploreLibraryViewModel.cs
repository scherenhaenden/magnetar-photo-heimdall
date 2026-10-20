using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Magnetar.Photo.Heimdall.Desktop.ViewModels;

public enum ExploreLibraryState
{
    Loading,
    Empty,
    Error,
    Populated
}

public sealed record ExploreLibraryRow(
    string Filename,
    string Extension,
    string RelativePath,
    long SizeBytes,
    DateTimeOffset CaptureDate);

public sealed class ExploreLibraryViewModel : INotifyPropertyChanged
{
    private ExploreLibraryState _currentState = ExploreLibraryState.Loading;
    private string _errorMessage = string.Empty;
    private string _searchText = string.Empty;
    private IReadOnlyList<ExploreLibraryRow> _allRows = Array.Empty<ExploreLibraryRow>();
    private readonly ObservableCollection<ExploreLibraryRow> _filteredRows = new();

    public ExploreLibraryState CurrentState
    {
        get => _currentState;
        private set
        {
            if (SetField(ref _currentState, value))
            {
                OnPropertyChanged(nameof(IsLoading));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(IsError));
                OnPropertyChanged(nameof(IsPopulated));
            }
        }
    }

    public bool IsLoading => CurrentState == ExploreLibraryState.Loading;
    public bool IsEmpty => CurrentState == ExploreLibraryState.Empty;
    public bool IsError => CurrentState == ExploreLibraryState.Error;
    public bool IsPopulated => CurrentState == ExploreLibraryState.Populated;

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
            {
                ApplyFilterAndSort();
            }
        }
    }

    public ObservableCollection<ExploreLibraryRow> FilteredRows => _filteredRows;

    public void SetLoading()
    {
        ErrorMessage = string.Empty;
        CurrentState = ExploreLibraryState.Loading;
    }

    public void SetError(string message)
    {
        ErrorMessage = message;
        CurrentState = ExploreLibraryState.Error;
    }

    public void SetRows(IEnumerable<ExploreLibraryRow> rows)
    {
        _allRows = rows.ToList();

        if (_allRows.Count == 0)
        {
            CurrentState = ExploreLibraryState.Empty;
        }
        else
        {
            CurrentState = ExploreLibraryState.Populated;
        }

        ApplyFilterAndSort();
    }

    private void ApplyFilterAndSort()
    {
        if (_allRows.Count == 0)
        {
            _filteredRows.Clear();
            return;
        }

        var query = _allRows.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            query = query.Where(r =>
                r.Filename.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                r.Extension.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                r.RelativePath.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
        }

        query = query.OrderBy(r => r.CaptureDate).ThenBy(r => r.Filename);

        _filteredRows.Clear();
        foreach (var row in query)
        {
            _filteredRows.Add(row);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
