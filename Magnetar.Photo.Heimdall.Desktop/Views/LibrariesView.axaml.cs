using Avalonia.Controls;

namespace Magnetar.Photo.Heimdall.Desktop.Views;

/// <summary>
/// Library catalogue and onboarding view. Its DataContext is the existing
/// <c>LibraryScreenViewModel</c>, keeping physical scan and SQLite work out of the view.
/// </summary>
public partial class LibrariesView : UserControl
{
    public LibrariesView() => InitializeComponent();
}
