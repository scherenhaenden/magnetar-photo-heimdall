using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Magnetar.Photo.Heimdall.Desktop.ViewModels.DuplicateDecision;

public sealed class DuplicateGroupViewModel : INotifyPropertyChanged
{
    private DuplicateAssetViewModel? _selectedAsset;

    public DuplicateGroupViewModel(string groupId, IEnumerable<DuplicateAssetViewModel> assets)
    {
        GroupId = groupId;
        Assets = new ObservableCollection<DuplicateAssetViewModel>(assets);

        foreach (var asset in Assets)
        {
            asset.PropertyChanged += Asset_PropertyChanged;
        }

        UpdateGroupProperties();
    }

    public string GroupId { get; }
    public ObservableCollection<DuplicateAssetViewModel> Assets { get; }

    public DuplicateAssetViewModel? SelectedAsset
    {
        get => _selectedAsset;
        set
        {
            if (_selectedAsset != value)
            {
                _selectedAsset = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsResolved => Assets.Count(a => a.IsKeeper) == 1;

    public long PotentialSavingsBytes => Assets.Count > 1 && Assets.Count(a => a.IsKeeper) == 1
        ? Assets.Where(a => !a.IsKeeper).Sum(a => a.SizeInBytes)
        : 0;

    public string Algorithm => Assets.FirstOrDefault()?.Algorithm ?? string.Empty;
    public string GroupHash => Assets.FirstOrDefault()?.Hash ?? string.Empty;
    public string RepresentativeFileName => Assets.FirstOrDefault()?.FileName ?? string.Empty;

    private void Asset_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DuplicateAssetViewModel.IsKeeper))
        {
            if (sender is DuplicateAssetViewModel changedAsset && changedAsset.IsKeeper)
            {
                // Enforce single keeper per group
                foreach (var asset in Assets)
                {
                    if (asset != changedAsset && asset.IsKeeper)
                    {
                        asset.IsKeeper = false;
                    }
                }
            }
            OnPropertyChanged(nameof(IsResolved));
            OnPropertyChanged(nameof(PotentialSavingsBytes));
        }
    }

    private void UpdateGroupProperties()
    {
        OnPropertyChanged(nameof(IsResolved));
        OnPropertyChanged(nameof(PotentialSavingsBytes));
        OnPropertyChanged(nameof(Algorithm));
        OnPropertyChanged(nameof(GroupHash));
        OnPropertyChanged(nameof(RepresentativeFileName));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
