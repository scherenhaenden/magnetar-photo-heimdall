using Magnetar.Photo.Heimdall.Desktop.ViewModels.PlanReview;
using Xunit;

namespace Magnetar.Photo.Heimdall.Desktop.IntegrationTests.ViewModels;

public sealed class ReviewPlanViewModelTests
{
    [Fact]
    public void InitialState_IsEmpty()
    {
        // Arrange & Act
        var vm = new ReviewPlanViewModel();

        // Assert
        Assert.False(vm.IsLoading);
        Assert.False(vm.HasError);
        Assert.Null(vm.ErrorMessage);
        Assert.True(vm.IsEmpty);
        Assert.False(vm.IsLoaded);
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
        Assert.Single(vm.Operations);
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
        Assert.Empty(vm.Operations);
    }
}
