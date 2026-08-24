using Avalonia.Platform;
using Magnetar.Photo.Heimdall.Desktop.Controls;
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
    public void Production_shell_resource_targets_only_the_dedicated_shell_control()
    {
        var resourceUri = new Uri("avares://Magnetar.Photo.Heimdall.Desktop/Design/HeimdallShell.axaml");
        using var resource = AssetLoader.Open(resourceUri);
        using var reader = new StreamReader(resource);
        var xaml = reader.ReadToEnd();

        Assert.Contains("TargetType=\"controls:HeimdallShell\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TargetType=\"ContentControl\"", xaml, StringComparison.Ordinal);
        Assert.True(typeof(Avalonia.Controls.ContentControl).IsAssignableFrom(typeof(HeimdallShell)));
    }
}
