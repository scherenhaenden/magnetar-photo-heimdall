using Avalonia.Controls;

namespace Magnetar.Photo.Heimdall.Desktop.Controls;

/// <summary>
/// The application's single, non-recursive chrome host.
/// A dedicated type prevents its template from being selected for the
/// <see cref="ContentControl"/> instances Avalonia creates while displaying page content.
/// </summary>
public sealed class HeimdallShell : ContentControl
{
}
