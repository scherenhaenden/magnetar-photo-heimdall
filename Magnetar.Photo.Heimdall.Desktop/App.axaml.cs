using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Magnetar.Photo.Heimdall.Desktop.ViewModels;
using Magnetar.Photo.Heimdall.Desktop.Views;

namespace Magnetar.Photo.Heimdall.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Create the window first so StorageProvider is available to the picker lambda.
            var mainWindow = new MainWindow();

            // Folder-picker delegate: captures the window reference but keeps the
            // ViewModel free of any Avalonia UI dependency.
            async Task<string?> PickFolderAsync()
            {
                var result = await mainWindow.StorageProvider.OpenFolderPickerAsync(
                    new FolderPickerOpenOptions
                    {
                        Title = "Choose library folder",
                        AllowMultiple = false,
                    });
                return result.Count > 0 ? result[0].TryGetLocalPath() : null;
            }

            async Task<string?> PickSshKeyAsync()
            {
                var result = await mainWindow.StorageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions
                    {
                        Title = "Elige la llave privada SSH para Heimdall",
                        AllowMultiple = false,
                        FileTypeFilter = [new FilePickerFileType("Llaves privadas SSH") { Patterns = ["*"] }],
                    });
                return result.Count > 0 ? result[0].TryGetLocalPath() : null;
            }

            var remoteConfiguration = DesktopComposition.CreateRemoteDuplicateAnalysisFacade();
            mainWindow.DataContext = new LibraryScreenViewModel(
                DesktopComposition.CreateOnboardingFacade(),
                PickFolderAsync,
                remoteConfiguration.Service,
                remoteConfiguration.RootId,
                remoteConfiguration.ConfigurationError,
                remoteConfiguration.Settings,
                PickSshKeyAsync,
                settings =>
                {
                    var configured = DesktopComposition.CreateRemoteDuplicateAnalysisFacade(settings);
                    return (configured.Service, configured.ConfigurationError);
                });

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
