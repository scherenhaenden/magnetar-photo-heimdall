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

            mainWindow.DataContext = new LibraryScreenViewModel(
                DesktopComposition.CreateOnboardingFacade(),
                PickFolderAsync);

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
