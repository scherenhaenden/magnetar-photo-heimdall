using Avalonia;
using Avalonia.Headless;
using Avalonia.Styling;
using Magnetar.Photo.Heimdall.Desktop;
using Magnetar.Photo.Heimdall.Desktop.Controls;
using Magnetar.Photo.Heimdall.Desktop.Views;
using Xunit;

namespace Magnetar.Photo.Heimdall.Desktop.IntegrationTests;

/// <summary>
/// Verifies the shipped production resource's shell contract. This protects the
/// UI from applying the shell template to ContentPresenter-created ContentControls,
/// which duplicates the application chrome.
/// </summary>
public sealed class HeimdallShellIntegrationTests
{
    [Fact]
    public void Production_window_loads_one_dedicated_non_recursive_shell()
    {
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();
        var application = (App)Application.Current!;
        application.Initialize();

        var window = new MainWindow();
        var shell = Assert.IsType<HeimdallShell>(window.Content);
        var theme = Assert.IsType<ControlTheme>(shell.Theme);

        Assert.Equal(typeof(HeimdallShell), theme.TargetType);
        Assert.True(typeof(Avalonia.Controls.ContentControl).IsAssignableFrom(typeof(HeimdallShell)));
    }
}
