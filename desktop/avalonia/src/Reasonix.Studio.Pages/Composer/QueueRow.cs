namespace Reasonix.Studio.Pages.Composer;

/// <summary>
/// One line the queue strip draws, with the actions that make sense for it.
/// </summary>
/// <param name="Id">The entry the actions name.</param>
/// <param name="Label">What this line is, in the reader's language.</param>
/// <param name="Preview">The first line of what it carries.</param>
/// <param name="Why">Why a held line stopped, when the kernel said.</param>
/// <param name="CanSteer">Whether the line can become guidance for the running turn.</param>
/// <param name="CanSendNow">Whether the line can be moved up and the turn stopped for it.</param>
/// <param name="CanRetry">Whether the kernel offers a retry that cannot repeat an effect.</param>
/// <param name="CanCancel">Whether the line can be taken back.</param>
public sealed record QueueRow(
    string Id,
    string Label,
    string Preview,
    string? Why,
    bool CanSteer,
    bool CanSendNow,
    bool CanRetry,
    bool CanCancel);
