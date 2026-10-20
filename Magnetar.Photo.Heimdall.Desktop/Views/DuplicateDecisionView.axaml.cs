using Avalonia.Controls;
using Magnetar.Photo.Heimdall.Desktop.ViewModels.DuplicateDecision;

namespace Magnetar.Photo.Heimdall.Desktop.Views;

/// <summary>Presentation-only duplicate comparison; it cannot execute or queue filesystem operations.</summary>
public partial class DuplicateDecisionView : UserControl
{
    public DuplicateDecisionView()
    {
        InitializeComponent();
    }
}
