namespace Reasonix.Studio.Pages.Rail;

/// <summary>
/// How deep a row sits in the sidebar's tree.
/// </summary>
public enum RailRowKind
{
    /// <summary>
    /// A project: the folder the conversations were saved under.
    /// </summary>
    Project,

    /// <summary>
    /// One conversation, or one of its copies or versions.
    /// </summary>
    Session,
}
