using Avalonia.Controls;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The window state to restore when leaving full screen.</summary>
    private WindowState _beforeFullScreen;

    /// <summary>Toggles the desktop window without losing its maximized state.</summary>
    private void ToggleFullScreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            WindowState = _beforeFullScreen;
            return;
        }

        _beforeFullScreen = WindowState;
        WindowState = WindowState.FullScreen;
    }
}
