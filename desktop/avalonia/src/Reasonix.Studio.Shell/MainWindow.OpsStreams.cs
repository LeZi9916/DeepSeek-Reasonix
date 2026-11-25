namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Applies the live-stream preference to the session coordinator.</summary>
    private void ReconcilePaneStreams()
    {
        if (_navigation is { } sessions)
        {
            sessions.StreamLimit = _workspacePreferences.LiveStreamLimit ?? 3;
            sessions.ReconcilePaneStreams();
        }
    }
}
