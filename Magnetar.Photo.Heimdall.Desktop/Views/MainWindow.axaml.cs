using Avalonia.Controls;
using Avalonia.Interactivity;
using Magnetar.Photo.Heimdall.Desktop.ViewModels;

namespace Magnetar.Photo.Heimdall.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void ShowExploreLibraryPreview(object? sender, RoutedEventArgs e) =>
        ShowPreview("Heimdall — Explore library", new ExploreLibraryView());

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
