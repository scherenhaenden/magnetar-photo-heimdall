using Avalonia.Controls;
using Avalonia.Interactivity;
using Magnetar.Photo.Heimdall.Desktop.ViewModels;

namespace Magnetar.Photo.Heimdall.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void ShowExploreLibraryPreview(object? sender, RoutedEventArgs e)
    {
        var vm = new ExploreLibraryViewModel();
        vm.SetRows(new[]
        {
            new ExploreLibraryRow("DSC_9482.CR3", "RAW", "Iceland/DSC_9482.CR3", 47395635, new DateTimeOffset(2020, 10, 14, 9, 42, 0, TimeSpan.Zero)),
            new ExploreLibraryRow("DSC_9483.JPG", "JPG", "Iceland/DSC_9483.JPG", 8193442, new DateTimeOffset(2020, 10, 14, 9, 43, 0, TimeSpan.Zero)),
            new ExploreLibraryRow("DSC_9484.CR3", "RAW+JPG", "Iceland/DSC_9484.CR3", 45293012, new DateTimeOffset(2020, 10, 14, 9, 45, 0, TimeSpan.Zero)),
            new ExploreLibraryRow("IMG_1201.HEIC", "HEIC", "iPhone/IMG_1201.HEIC", 2045931, new DateTimeOffset(2020, 10, 15, 12, 10, 0, TimeSpan.Zero)),
            new ExploreLibraryRow("DJI_0210.MOV", "MOV", "Drone/DJI_0210.MOV", 839402945, new DateTimeOffset(2020, 10, 16, 14, 20, 0, TimeSpan.Zero)),
            new ExploreLibraryRow("DSC_9485.ARW", "RAW", "Sony/DSC_9485.ARW", 49203948, new DateTimeOffset(2020, 10, 17, 16, 30, 0, TimeSpan.Zero))
        });

        ShowPreview("Heimdall — Explore library", new ExploreLibraryView { DataContext = vm });
    }

    private void ShowDuplicateDecisionPreview(object? sender, RoutedEventArgs e) =>
        ShowPreview("Heimdall — Review duplicates", new DuplicateDecisionView());

    private void ShowOrganizePreview(object? sender, RoutedEventArgs e) =>
        ShowPreview("Heimdall — Organize photos", new OrganizeView());

    private void ShowReviewPlanPreview(object? sender, RoutedEventArgs e) =>
        ShowPreview("Heimdall — Review plan", new ReviewPlanView());

    private void ShowLibrariesPreview(object? sender, RoutedEventArgs e) =>
        ShowPreview("Heimdall — Source libraries", new LibrariesView { DataContext = LibraryScreen });

    private void ShowSummaryPreview(object? sender, RoutedEventArgs e) =>
        ShowPreview("Heimdall — Summary", new SummaryView { DataContext = new SummaryScreenViewModel(LibraryScreen) });

    private LibraryScreenViewModel LibraryScreen => DataContext as LibraryScreenViewModel
        ?? throw new InvalidOperationException("MainWindow requires LibraryScreenViewModel as its DataContext.");

    private void ShowPreview(string title, Control content)
    {
        var preview = new Window
        {
            Title = title,
            Width = 1180,
            Height = 760,
            MinWidth = 920,
            MinHeight = 580,
            Content = content,
        };

        preview.Show(this);
    }
}
