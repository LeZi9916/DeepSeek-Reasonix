using Avalonia;

namespace Reasonix.Studio.Shell;

/// <summary>
/// Hosts the process entry point for Reasonix Studio.
/// </summary>
public static class Program
{
    /// <summary>
    /// Starts the Avalonia desktop lifetime for Reasonix Studio.
    /// </summary>
    /// <param name="args">Command-line arguments passed to the shell.</param>
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Builds the Avalonia application configuration used by the designer and the desktop lifetime.
    /// </summary>
    /// <returns>The configured <see cref="AppBuilder"/> instance.</returns>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
