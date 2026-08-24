using Avalonia.Controls;
using Avalonia.Interactivity;

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
