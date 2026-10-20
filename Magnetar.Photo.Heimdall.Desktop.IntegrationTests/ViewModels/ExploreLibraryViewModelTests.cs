using System;
using Xunit;
using Magnetar.Photo.Heimdall.Desktop.ViewModels;

namespace Magnetar.Photo.Heimdall.Desktop.IntegrationTests.ViewModels;

public sealed class ExploreLibraryViewModelTests
{
    [Fact]
    public void InitialState_IsLoading()
    {
        var vm = new ExploreLibraryViewModel();
        Assert.True(vm.IsLoading);
        Assert.False(vm.IsPopulated);
        Assert.False(vm.IsEmpty);
        Assert.False(vm.IsError);
        Assert.Equal(ExploreLibraryState.Loading, vm.CurrentState);
    }

    [Fact]
    public void SetError_TransitionsToErrorState()
    {
        var vm = new ExploreLibraryViewModel();
        vm.SetError("Network failure");

        Assert.True(vm.IsError);
        Assert.False(vm.IsLoading);
        Assert.Equal("Network failure", vm.ErrorMessage);
    }

    [Fact]
    public void SetRows_WithEmptyList_TransitionsToEmptyState()
    {
        var vm = new ExploreLibraryViewModel();
        vm.SetRows(Array.Empty<ExploreLibraryRow>());

        Assert.True(vm.IsEmpty);
        Assert.Empty(vm.FilteredRows);
    }

    [Fact]
    public void SetRows_WithItems_TransitionsToPopulatedState()
    {
        var vm = new ExploreLibraryViewModel();
        vm.SetRows(new[] { CreateRow("img1.jpg") });

        Assert.True(vm.IsPopulated);
        Assert.Single(vm.FilteredRows);
    }

    [Fact]
    public void Filtering_AppliesToFilenameExtensionAndPath()
    {
        var vm = new ExploreLibraryViewModel();
        vm.SetRows(new[]
        {
            CreateRow("photo1.jpg", "JPG", "holiday/photo1.jpg"),
            CreateRow("photo2.png", "PNG", "holiday/photo2.png"),
            CreateRow("doc1.pdf", "PDF", "work/doc1.pdf")
        });

        // Search filename
        vm.SearchText = "photo1";
        Assert.Single(vm.FilteredRows);
        Assert.Equal("photo1.jpg", vm.FilteredRows[0].Filename);

        // Search extension (case insensitive)
        vm.SearchText = "png";
        Assert.Single(vm.FilteredRows);
        Assert.Equal("photo2.png", vm.FilteredRows[0].Filename);

        // Search path
        vm.SearchText = "work";
        Assert.Single(vm.FilteredRows);
        Assert.Equal("doc1.pdf", vm.FilteredRows[0].Filename);
    }

    [Fact]
    public void Sorting_OrdersByCaptureDateThenFilename()
    {
        var vm = new ExploreLibraryViewModel();
        vm.SetRows(new[]
        {
            CreateRow("B.jpg", captureDate: new DateTimeOffset(2023, 1, 1, 10, 0, 0, TimeSpan.Zero)),
            CreateRow("A.jpg", captureDate: new DateTimeOffset(2023, 1, 1, 10, 0, 0, TimeSpan.Zero)),
            CreateRow("C.jpg", captureDate: new DateTimeOffset(2022, 1, 1, 10, 0, 0, TimeSpan.Zero))
        });

        Assert.Equal(3, vm.FilteredRows.Count);
        Assert.Equal("C.jpg", vm.FilteredRows[0].Filename); // Earliest date
        Assert.Equal("A.jpg", vm.FilteredRows[1].Filename); // Same date, A comes before B
        Assert.Equal("B.jpg", vm.FilteredRows[2].Filename);
    }

    private static ExploreLibraryRow CreateRow(
        string filename,
        string extension = "JPG",
        string path = "path",
        long size = 1000,
        DateTimeOffset? captureDate = null)
    {
        return new ExploreLibraryRow(
            filename,
            extension,
            path,
            size,
            captureDate ?? DateTimeOffset.UtcNow);
    }
}
