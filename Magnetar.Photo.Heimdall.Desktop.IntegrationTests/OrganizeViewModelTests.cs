using Magnetar.Photo.Heimdall.Desktop.ViewModels;
using Xunit;

namespace Magnetar.Photo.Heimdall.Desktop.IntegrationTests;

public class OrganizeViewModelTests
{
    [Fact]
    public void OrganizeViewModel_DefaultState_IsValid()
    {
        var vm = new OrganizeViewModel();

        Assert.Equal(string.Empty, vm.FolderPattern);
        Assert.Equal(string.Empty, vm.FilePattern);
        Assert.False(vm.IsFolderPatternValid);
        Assert.False(vm.IsFilePatternValid);
        Assert.Equal(0, vm.NoConflictCount);
        Assert.Equal(0, vm.RequiresReviewCount);
        Assert.Equal(0, vm.AlreadyOrganizedCount);
        Assert.False(vm.HasPreviewData);
        Assert.Empty(vm.PreviewItems);
        Assert.Equal(0, vm.MissingMetadataStrategyIndex);
        Assert.Equal(0, vm.CollisionStrategyIndex);
    }

    [Fact]
    public void OrganizeViewModel_SetFolderPattern_UpdatesValidationAndRaisesPropertyChanged()
    {
        var vm = new OrganizeViewModel();
        var propertyChanges = new List<string?>();
        vm.PropertyChanged += (s, e) => propertyChanges.Add(e.PropertyName);

        vm.FolderPattern = "NewPattern";

        Assert.Equal("NewPattern", vm.FolderPattern);
        Assert.True(vm.IsFolderPatternValid);
        Assert.Contains(nameof(vm.FolderPattern), propertyChanges);
        Assert.Contains(nameof(vm.IsFolderPatternValid), propertyChanges);
    }

    [Fact]
    public void OrganizeViewModel_SetFilePattern_UpdatesValidationAndRaisesPropertyChanged()
    {
        var vm = new OrganizeViewModel();
        var propertyChanges = new List<string?>();
        vm.PropertyChanged += (s, e) => propertyChanges.Add(e.PropertyName);

        vm.FilePattern = "NewFilePattern";

        Assert.Equal("NewFilePattern", vm.FilePattern);
        Assert.True(vm.IsFilePatternValid);
        Assert.Contains(nameof(vm.FilePattern), propertyChanges);
        Assert.Contains(nameof(vm.IsFilePatternValid), propertyChanges);
    }

    [Fact]
    public void OrganizeViewModel_SetStrategies_RaisesPropertyChanged()
    {
        var vm = new OrganizeViewModel();
        var propertyChanges = new List<string?>();
        vm.PropertyChanged += (s, e) => propertyChanges.Add(e.PropertyName);

        vm.MissingMetadataStrategyIndex = 1;
        vm.CollisionStrategyIndex = 2;

        Assert.Equal(1, vm.MissingMetadataStrategyIndex);
        Assert.Equal(2, vm.CollisionStrategyIndex);
        Assert.Contains(nameof(vm.MissingMetadataStrategyIndex), propertyChanges);
        Assert.Contains(nameof(vm.CollisionStrategyIndex), propertyChanges);
    }
}
