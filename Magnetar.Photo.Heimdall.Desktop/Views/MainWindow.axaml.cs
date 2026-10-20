using Avalonia.Controls;
using Avalonia.VisualTree;
using Magnetar.Photo.Heimdall.Desktop.Controls;

namespace Magnetar.Photo.Heimdall.Desktop.Views;

public partial class MainWindow : Window
{
    private HeimdallShell? _shell;
    private Control? _librariesPage;

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => WireNavigation();
    }

    private void WireNavigation()
    {
        _shell ??= this.GetVisualDescendants().OfType<HeimdallShell>().FirstOrDefault();
        if (_shell is null) return;
        _librariesPage ??= _shell.Content as Control;

        foreach (var button in _shell.GetVisualDescendants().OfType<Button>().Where(button => button.Tag is string))
        {
            if (Equals(button.Tag, "Unavailable"))
            {
                button.IsEnabled = false;
                continue;
            }

            button.Click -= OnNavigationClick;
            button.Click += OnNavigationClick;
        }
    }

    private void OnNavigationClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string section } || _shell is null) return;
        Control? page = section switch
        {
            "Libraries" or "Duplicates" or "Settings" => _librariesPage,
            "Summary" => new SummaryView(),
            "Explore" => new ExploreLibraryView(),
            "Organize" => new OrganizeView(),
            _ => null,
        };
        if (page is null) return;
        page.DataContext = DataContext;
        _shell.Content = page;
    }
}
