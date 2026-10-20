using System.Linq;
using Magnetar.Photo.Heimdall.Desktop.ViewModels.DuplicateDecision;
using Xunit;

namespace Magnetar.Photo.Heimdall.Desktop.IntegrationTests;

public sealed class DuplicateDecisionViewModelTests
{
    [Fact]
    public void Empty_state_reflects_no_groups()
    {
        var vm = new DuplicateDecisionViewModel();

        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasData);
        Assert.False(vm.AllGroupsResolved);
        Assert.Equal(0, vm.TotalGroupsCount);
        Assert.Equal(0, vm.TotalPotentialSavingsBytes);
    }

    [Fact]
    public void Loading_state_hides_data_and_empty_states()
    {
        var vm = new DuplicateDecisionViewModel();
        vm.IsLoading = true;

        Assert.True(vm.IsLoading);
        Assert.False(vm.IsEmpty);
        Assert.False(vm.HasData);
        Assert.False(vm.HasError);
    }

    [Fact]
    public void Error_state_displays_error_message()
    {
        var vm = new DuplicateDecisionViewModel();
        vm.HasError = true;
        vm.ErrorMessage = "Failed to load";

        Assert.True(vm.HasError);
        Assert.Equal("Failed to load", vm.ErrorMessage);
        Assert.False(vm.IsEmpty);
        Assert.False(vm.HasData);
    }

    [Fact]
    public void Group_resolution_requires_exactly_one_keeper()
    {
        var asset1 = new DuplicateAssetViewModel("path1", 100, "SHA", "1", "hash");
        var asset2 = new DuplicateAssetViewModel("path2", 100, "SHA", "1", "hash");
        var group = new DuplicateGroupViewModel("g1", new[] { asset1, asset2 });

        Assert.False(group.IsResolved);

        asset1.IsKeeper = true;
        Assert.True(group.IsResolved);

        asset2.IsKeeper = true;
        // The DuplicateGroupViewModel enforces single keeper, so asset1 should now be false
        Assert.True(group.IsResolved);
        Assert.False(asset1.IsKeeper);
        Assert.True(asset2.IsKeeper);

        asset2.IsKeeper = false;
        Assert.False(group.IsResolved);
    }

    [Fact]
    public void Potential_savings_calculated_correctly()
    {
        var asset1 = new DuplicateAssetViewModel("path1", 1000, "SHA", "1", "hash");
        var asset2 = new DuplicateAssetViewModel("path2", 1000, "SHA", "1", "hash");
        var asset3 = new DuplicateAssetViewModel("path3", 1000, "SHA", "1", "hash");
        var group = new DuplicateGroupViewModel("g1", new[] { asset1, asset2, asset3 });

        // No keeper selected, all are duplicates theoretically but until a keeper is chosen,
        // all non-keepers count towards savings. Since no keeper is selected, all 3 are non-keepers: 3000
        Assert.Equal(3000, group.PotentialSavingsBytes);

        asset1.IsKeeper = true;
        // 1 keeper (1000), 2 non-keepers (2000). Potential savings should be 2000.
        Assert.Equal(2000, group.PotentialSavingsBytes);
    }

    [Fact]
    public void Decision_view_model_aggregates_group_metrics()
    {
        var asset1 = new DuplicateAssetViewModel("path1", 1000, "SHA", "1", "hash");
        var asset2 = new DuplicateAssetViewModel("path2", 1000, "SHA", "1", "hash");
        var group1 = new DuplicateGroupViewModel("g1", new[] { asset1, asset2 });

        var asset3 = new DuplicateAssetViewModel("path3", 500, "SHA", "1", "hash2");
        var asset4 = new DuplicateAssetViewModel("path4", 500, "SHA", "1", "hash2");
        var group2 = new DuplicateGroupViewModel("g2", new[] { asset3, asset4 });

        var vm = new DuplicateDecisionViewModel();
        vm.LoadGroups(new[] { group1, group2 });

        Assert.True(vm.HasData);
        Assert.Equal(2, vm.TotalGroupsCount);
        Assert.False(vm.AllGroupsResolved);
        Assert.Equal(3000, vm.TotalPotentialSavingsBytes); // 2000 + 1000

        asset1.IsKeeper = true;
        Assert.False(vm.AllGroupsResolved);
        Assert.Equal(2000, vm.TotalPotentialSavingsBytes); // 1000 + 1000

        asset3.IsKeeper = true;
        Assert.True(vm.AllGroupsResolved);
        Assert.Equal(1500, vm.TotalPotentialSavingsBytes); // 1000 + 500
    }
}
