namespace Reasonix.Studio.Shell.Session;

/// <summary>A peer thread state attached to a retained pane tab.</summary>
/// <param name="State">Kernel peer-thread state.</param>
/// <param name="Title">Peer conversation title.</param>
/// <param name="Project">Peer project name.</param>
internal sealed record PeerTabMark(string State, string Title, string Project);
