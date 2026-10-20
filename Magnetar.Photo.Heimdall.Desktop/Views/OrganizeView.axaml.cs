using Avalonia.Controls;
using Magnetar.Photo.Heimdall.Desktop.ViewModels;

namespace Magnetar.Photo.Heimdall.Desktop.Views;

/// <summary>Presentation-only editor for a future reversible organization plan.</summary>
public partial class OrganizeView : UserControl
{
    public OrganizeView()
    {
        InitializeComponent();
        DataContext = new OrganizeViewModel();
    }
}
