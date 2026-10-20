using System;
using System.Collections.Generic;
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
        var asset2 = new DuplicateAssetViewModel("path2", 1200, "SHA", "1", "hash");
        var asset3 = new DuplicateAssetViewModel("path3", 1400, "SHA", "1", "hash");
        var group = new DuplicateGroupViewModel("g1", new[] { asset1, asset2, asset3 });

        // An unresolved group has no confirmed removable files yet.
        Assert.Equal(0, group.PotentialSavingsBytes);

        asset2.IsKeeper = true;
        Assert.Equal(2400, group.PotentialSavingsBytes);
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
        Assert.Equal(0, vm.TotalPotentialSavingsBytes);

        asset1.IsKeeper = true;
        Assert.False(vm.AllGroupsResolved);
        Assert.Equal(1000, vm.TotalPotentialSavingsBytes);

        asset3.IsKeeper = true;
        Assert.True(vm.AllGroupsResolved);
        Assert.Equal(1500, vm.TotalPotentialSavingsBytes);
    }

    [Fact]
    public void Load_groups_selects_first_group_and_clears_selection_when_empty()
    {
        var first = new DuplicateGroupViewModel("first", new[]
        {
            new DuplicateAssetViewModel("path1", 100, "BLAKE3", "1", "hash1"),
            new DuplicateAssetViewModel("path2", 100, "BLAKE3", "1", "hash1")
        });
        var second = new DuplicateGroupViewModel("second", new[]
        {
            new DuplicateAssetViewModel("path3", 200, "BLAKE3", "1", "hash2"),
            new DuplicateAssetViewModel("path4", 200, "BLAKE3", "1", "hash2")
        });
        var vm = new DuplicateDecisionViewModel();

        vm.LoadGroups(new[] { first, second });

        Assert.Same(first, vm.SelectedGroup);

        vm.LoadGroups(Array.Empty<DuplicateGroupViewModel>());

        Assert.Null(vm.SelectedGroup);
    }

    [Fact]
    public void Load_groups_supports_the_current_groups_collection_as_input()
    {
        var first = new DuplicateGroupViewModel("first", new[]
        {
            new DuplicateAssetViewModel("path1", 100, "BLAKE3", "1", "hash1"),
            new DuplicateAssetViewModel("path2", 100, "BLAKE3", "1", "hash1")
        });
        var vm = new DuplicateDecisionViewModel();
        vm.LoadGroups(new[] { first });

        vm.LoadGroups(vm.Groups);

        Assert.Single(vm.Groups);
        Assert.Same(first, vm.Groups[0]);
        Assert.Same(first, vm.SelectedGroup);
    }

    [Fact]
    public void Load_groups_preserves_existing_state_when_input_enumeration_throws()
    {
        var existing = new DuplicateGroupViewModel("existing", new[]
        {
            new DuplicateAssetViewModel("path1", 100, "BLAKE3", "1", "hash1"),
            new DuplicateAssetViewModel("path2", 100, "BLAKE3", "1", "hash1")
        });
        var replacement = new DuplicateGroupViewModel("replacement", new[]
        {
            new DuplicateAssetViewModel("path3", 200, "BLAKE3", "1", "hash2"),
            new DuplicateAssetViewModel("path4", 200, "BLAKE3", "1", "hash2")
        });
        var vm = new DuplicateDecisionViewModel();
        vm.LoadGroups(new[] { existing });

        Assert.Throws<InvalidOperationException>(() => vm.LoadGroups(ThrowAfterYielding(replacement)));

        Assert.Single(vm.Groups);
        Assert.Same(existing, vm.Groups[0]);
        Assert.Same(existing, vm.SelectedGroup);
    }

    private static IEnumerable<DuplicateGroupViewModel> ThrowAfterYielding(DuplicateGroupViewModel group)
    {
        yield return group;
        throw new InvalidOperationException("Enumeration failed.");
    }
}
