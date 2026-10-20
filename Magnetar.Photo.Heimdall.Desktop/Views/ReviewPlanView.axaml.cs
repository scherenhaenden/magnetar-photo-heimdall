using Avalonia.Controls;
using Magnetar.Photo.Heimdall.Desktop.ViewModels.PlanReview;

namespace Magnetar.Photo.Heimdall.Desktop.Views;

/// <summary>Presentation-only review surface; it deliberately cannot approve or execute a plan.</summary>
public partial class ReviewPlanView : UserControl
{
    public ReviewPlanView()
    {
        InitializeComponent();
        if (DataContext == null)
        {
            DataContext = new ReviewPlanViewModel();
        }
    }
}
