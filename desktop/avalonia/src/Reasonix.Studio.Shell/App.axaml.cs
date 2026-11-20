using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Reasonix.Studio.Shell;

/// <summary>
/// Describes the application object Avalonia creates for Reasonix Studio.
/// </summary>
public sealed partial class App : Application
{
    /// <summary>
    /// Loads the XAML that declares this application's styles.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Creates the main window once the desktop lifetime has started.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
