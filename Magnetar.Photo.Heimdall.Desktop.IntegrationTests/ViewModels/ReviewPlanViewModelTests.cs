using Magnetar.Photo.Heimdall.Desktop.ViewModels.PlanReview;
using Xunit;

namespace Magnetar.Photo.Heimdall.Desktop.IntegrationTests.ViewModels;

public sealed class ReviewPlanViewModelTests
{
    [Fact]
    public void InitialState_IsUnloaded()
    {
        // Arrange & Act
        var vm = new ReviewPlanViewModel();

        // Assert
        Assert.False(vm.IsLoading);
        Assert.False(vm.HasError);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.IsEmpty);
        Assert.False(vm.IsLoaded);
        Assert.True(vm.IsUnloaded);
        Assert.Empty(vm.Operations);
    }

    [Fact]
    public void SetLoading_UpdatesState()
    {
        // Arrange
        var vm = new ReviewPlanViewModel();

        // Act
        vm.SetLoading();

        // Assert
        Assert.True(vm.IsLoading);
        Assert.False(vm.HasError);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.IsEmpty);
        Assert.False(vm.IsLoaded);
        Assert.False(vm.IsUnloaded);
    }

    [Fact]
    public void SetError_UpdatesState()
    {
        // Arrange
        var vm = new ReviewPlanViewModel();
        const string error = "Network failure";

        // Act
        vm.SetError(error);

        // Assert
        Assert.False(vm.IsLoading);
        Assert.True(vm.HasError);
        Assert.Equal(error, vm.ErrorMessage);
        Assert.False(vm.IsEmpty);
        Assert.False(vm.IsLoaded);
        Assert.False(vm.IsUnloaded);
    }

    [Fact]
    public void SetPlan_WithItems_UpdatesState()
    {
        // Arrange
        var vm = new ReviewPlanViewModel();
        var ops = new[]
        {
            new PlanOperationViewModel("source.jpg", "dest.jpg", "Copy", null)
        };

        // Act
        vm.SetPlan(ops);

        // Assert
        Assert.False(vm.IsLoading);
        Assert.False(vm.HasError);
        Assert.False(vm.IsEmpty);
        Assert.True(vm.IsLoaded);
        Assert.False(vm.IsUnloaded);
        var operation = Assert.Single(vm.Operations);
        Assert.Equal(ops[0].Source, operation.Source);
        Assert.Equal(ops[0].Destination, operation.Destination);
        Assert.Equal(ops[0].Action, operation.Action);
        Assert.Equal(ops[0].Warning, operation.Warning);
    }

    [Fact]
    public void SetPlan_WithEmptyList_SetsEmptyState()
    {
        // Arrange
        var vm = new ReviewPlanViewModel();

        // Act
        vm.SetPlan(Array.Empty<PlanOperationViewModel>());

        // Assert
        Assert.False(vm.IsLoading);
        Assert.False(vm.HasError);
        Assert.True(vm.IsEmpty);
        Assert.False(vm.IsLoaded);
        Assert.False(vm.IsUnloaded);
        Assert.Empty(vm.Operations);
    }

    [Fact]
    public void SetPlan_WhenEnumerationThrows_PreservesPreviousPlanAndState()
    {
        var vm = new ReviewPlanViewModel();
        var existing = new PlanOperationViewModel("old-source", "old-destination", "Copy", "Keep me");
        vm.SetPlan([existing]);

        Assert.Throws<InvalidOperationException>(() => vm.SetPlan(ThrowAfterOne()));

        var operation = Assert.Single(vm.Operations);
        Assert.Same(existing, operation);
        Assert.True(vm.IsLoaded);
        Assert.False(vm.IsEmpty);
        Assert.False(vm.HasError);
    }

    [Fact]
    public void Operations_CannotBeMutatedByConsumers()
    {
        var vm = new ReviewPlanViewModel();
        vm.SetPlan([new PlanOperationViewModel("source", "destination", "Copy")]);

        Assert.IsAssignableFrom<System.Collections.ObjectModel.ReadOnlyObservableCollection<PlanOperationViewModel>>(vm.Operations);
    }

    private static IEnumerable<PlanOperationViewModel> ThrowAfterOne()
    {
        yield return new PlanOperationViewModel("partial-source", "partial-destination", "Move");
        throw new InvalidOperationException("Synthetic enumeration failure.");
    }
}
