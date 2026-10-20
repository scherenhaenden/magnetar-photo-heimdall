using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Magnetar.Photo.Heimdall.Desktop.ViewModels.DuplicateDecision;

public sealed class DuplicateDecisionViewModel : INotifyPropertyChanged
{
    private bool _isLoading;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private DuplicateGroupViewModel? _selectedGroup;

    public DuplicateDecisionViewModel()
    {
        Groups = new ObservableCollection<DuplicateGroupViewModel>();
        Groups.CollectionChanged += (s, e) => UpdateAggregateProperties();
    }

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; }

    public DuplicateGroupViewModel? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (_selectedGroup != value)
            {
                _selectedGroup = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading != value)
            {
                _isLoading = value;
                OnPropertyChanged();
                UpdateAggregateProperties();
            }
        }
    }

    public bool HasError
    {
        get => _hasError;
        set
        {
            if (_hasError != value)
            {
                _hasError = value;
                OnPropertyChanged();
                UpdateAggregateProperties();
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (_errorMessage != value)
            {
                _errorMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsEmpty => !IsLoading && !HasError && Groups.Count == 0;
    public bool HasData => !IsLoading && !HasError && Groups.Count > 0;

    public bool AllGroupsResolved => Groups.Count > 0 && Groups.All(g => g.IsResolved);

    public long TotalPotentialSavingsBytes => Groups.Sum(g => g.PotentialSavingsBytes);
    public int TotalGroupsCount => Groups.Count;

    public void LoadGroups(IEnumerable<DuplicateGroupViewModel> groups)
    {
        foreach (var group in Groups)
        {
            group.PropertyChanged -= Group_PropertyChanged;
        }

        Groups.Clear();

        foreach (var group in groups)
        {
            group.PropertyChanged += Group_PropertyChanged;
            Groups.Add(group);
        }
        UpdateAggregateProperties();
    }

    private void Group_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DuplicateGroupViewModel.IsResolved) ||
            e.PropertyName == nameof(DuplicateGroupViewModel.PotentialSavingsBytes))
        {
            UpdateAggregateProperties();
        }
    }

    private void UpdateAggregateProperties()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(AllGroupsResolved));
        OnPropertyChanged(nameof(TotalPotentialSavingsBytes));
        OnPropertyChanged(nameof(TotalGroupsCount));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
