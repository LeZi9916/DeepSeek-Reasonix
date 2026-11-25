using Avalonia;
using Reasonix.Studio.Contracts.Diagnostics;
using Reasonix.Studio.Shell.Host;
using System;

namespace Reasonix.Studio.Shell;

/// <summary>
/// Hosts the process entry point for Reasonix Studio.
/// </summary>
/// <remarks>
/// The launch order is the whole of "one shell per machine": the claim comes
/// first, a second process raises the first and exits, and only the first writes
/// a log, starts the interface and starts a kernel. Doing the claim after the
/// window existed would mean two kernels opening one transcript before either
/// noticed.
/// </remarks>
public static class Program
{
    /// <summary>Gets the process entry timestamp when tracing is active.</summary>
    internal static long BootStarted
    {
        get; private set;
    }

    /// <summary>
    /// Starts the Avalonia desktop lifetime for Reasonix Studio.
    /// </summary>
    /// <param name="args">Command-line arguments passed to the shell.</param>
    /// <returns>The process's exit code.</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        using var performance = StudioPerformanceLog.Open();
        BootStarted = StudioPerformance.Timestamp();
        var identity = InstanceActivation.Identity();

        using var claim = new SingleInstance(identity);
        if (!claim.IsFirst)
        {
            // Another shell owns this install. Asking it to raise its window is
            // the whole job of a second launch; a window of its own would be a
            // second writer for one transcript.
            ShellLog.Line("second launch: asked the running shell to raise its window");
            InstanceActivation.Raise(identity);
            return 0;
        }

        ShellLog.Line("shell start " + string.Join(' ', args));
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ShellLog.Line("unhandled: " + e.ExceptionObject);

        using var activation = new InstanceActivation(identity, listen: true);
        App.Activation = activation;

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception error)
        {
            // A shell that will not start has nothing on screen to say why; the
            // log is the only account of it.
            ShellLog.Line("fatal: " + error);
            return 1;
        }
        finally
        {
            App.Activation = null;
            ShellLog.Line("shell stop");
        }
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
