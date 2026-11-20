using Reasonix.Studio.Kernel.Session;

namespace Reasonix.Studio.Shell.Session;

/// <summary>
/// What a launch produced: an engine, or the reason there is none.
/// </summary>
/// <param name="Engine">The conversation, when a kernel answered.</param>
/// <param name="Problem">Why there is no conversation, in the person's terms.</param>
public sealed record SessionLaunch(SessionEngine? Engine, string? Problem)
{
    /// <summary>
    /// Gets a value indicating whether a conversation was started.
    /// </summary>
    public bool Started => Engine is not null;
}
