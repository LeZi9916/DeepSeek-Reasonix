using Avalonia.Controls;
using Avalonia.Threading;
using Reasonix.Studio.Shell.Host;
using System;
using System.IO;

namespace Reasonix.Studio.Shell;

public sealed partial class App
{
    /// <summary>Connects a supported system registrar to the lifetime of the desktop window.</summary>
    /// <param name="window">The window the shortcut shows or hides.</param>
    private static void ConfigureGlobalShortcut(MainWindow window)
    {
        Func<ShortcutChord, Action, Action?>? register = null;
        if (OperatingSystem.IsMacOS())
        {
            register = MacShortcut.Register;
        }
        else if (OperatingSystem.IsWindows())
        {
            register = (chord, press) => WindowsShortcut.Register(window, chord, press);
        }

        if (register is null)
        {
            return;
        }

        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Reasonix", "Studio", "global-shortcut");
        string saved;
        try
        {
            saved = File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            saved = string.Empty;
        }

        var shortcut = new GlobalShortcut(saved, register, Save, () => Dispatcher.UIThread.Post(Toggle));
        window.WindowShortcut = shortcut;
        window.Closed += (_, _) =>
        {
            window.WindowShortcut = null;
            shortcut.Dispose();
        };

        void Save(string binding)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, binding);
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        void Toggle()
        {
            if (window.WindowShortcut is null)
            {
                return;
            }

            if (window.IsVisible && window.IsActive && window.WindowState != WindowState.Minimized)
            {
                window.Hide();
            }
            else
            {
                Raise(window);
            }
        }
    }
}
