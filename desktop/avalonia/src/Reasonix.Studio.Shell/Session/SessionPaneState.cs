using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Session;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Session;

/// <summary>One pane's retained runtime, status and reversible reading state.</summary>
/// <param name="engine">The actual runtime engine.</param><param name="status">Acknowledged session status.</param>
internal sealed class SessionPaneState(SessionEngine engine, SessionStatus status)
{
    /// <summary>Gets the retained runtime engine.</summary>
    internal SessionEngine Engine { get; } = engine;

    /// <summary>Gets or sets the acknowledged session status.</summary>
    internal SessionStatus Status { get; set; } = status;

    /// <summary>Gets the canonical remote machine, or empty for local.</summary>
    internal string Host => Engine.RemoteHost.Length > 0 ? Engine.RemoteHost : Engine.Runtime?.Host ?? string.Empty;

    /// <summary>Gets or sets the latest status request's publication ticket.</summary>
    internal long StatusRead
    {
        get; set;
    }

    /// <summary>Gets or sets a value indicating whether a background turn has unread results.</summary>
    internal bool Unread
    {
        get; set;
    }

    /// <summary>Gets or sets the single event subscription owned by this pane.</summary>
    internal Task? Watching
    {
        get; set;
    }

    /// <summary>Gets or sets the reader's retained transcript position.</summary>
    internal double ScrollOffset
    {
        get; set;
    }

    /// <summary>Gets or sets a value indicating whether the pane's workbench is open.</summary>
    internal bool WorkbenchOpen
    {
        get; set;
    }

    /// <summary>Gets or sets the last file selected in the pane's workbench.</summary>
    internal string? FilePath
    {
        get; set;
    }
}
