using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages;
using Reasonix.Studio.State.Transcript;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Session;

/// <summary>One pane's retained runtime, status and reversible reading state.</summary>
/// <param name="engine">The actual runtime engine.</param><param name="status">Acknowledged session status.</param>
internal sealed class SessionPaneState(SessionEngine engine, SessionStatus status)
{
    /// <summary>Gets the retained runtime engine.</summary>
    internal SessionEngine Engine { get; } = engine;

    /// <summary>Gets or sets the acknowledged session status.</summary>
    internal SessionStatus Status
    {
        get; set;
    } = status;

    /// <summary>Gets or sets a value indicating whether the host returned status for this binding.</summary>
    internal bool StatusConfirmed { get; set; } = true;

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

    /// <summary>Gets or sets a value indicating whether engine events are wired to the window.</summary>
    internal bool WatchBound
    {
        get; set;
    }

    /// <summary>Gets or sets the most recent foreground use for stream ordering.</summary>
    internal DateTimeOffset WatchedAt
    {
        get; set;
    } = DateTimeOffset.UtcNow;

    /// <summary>Gets or sets the active streaming period, if the pane has a slot.</summary>
    internal CancellationTokenSource? StreamCancellation
    {
        get; set;
    }

    /// <summary>Gets or sets the single event subscription owned by this pane.</summary>
    internal Task? Watching
    {
        get; set;
    }

    /// <summary>Gets or sets the last runtime status projected into the rail and tabs.</summary>
    internal (long Revision, int Count, bool Running, bool Waiting)? RunPresentation
    {
        get; set;
    }

    /// <summary>Gets or sets the reader's retained transcript position.</summary>
    internal (string? Id, double Offset, double Inset) ScrollAnchor
    {
        get; set;
    }

    /// <summary>Gets the disclosure choices for this transcript while its pane is retained.</summary>
    internal TranscriptDisclosureChoices DisclosureChoices { get; } = new();

    /// <summary>Gets the divider position while this pane is retained.</summary>
    internal NewAnswerBoundary AnswerBoundary { get; } = new();

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
