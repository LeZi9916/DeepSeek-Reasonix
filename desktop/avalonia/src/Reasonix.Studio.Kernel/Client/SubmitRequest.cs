namespace Reasonix.Studio.Kernel.Client;

/// <summary>
/// A request to start a turn, in the shape the kernel's inbox takes.
/// </summary>
/// <param name="Input">The line as the person typed it.</param>
/// <param name="Submit">What the model reads, with any chips resolved.</param>
/// <param name="Intent">
/// What the line becomes: <c>followup</c> to start or queue a turn, <c>steer</c>
/// to slip it into the running one at its next tool boundary.
/// </param>
/// <remarks>
/// The intent is the caller's decision, not the kernel's guess: sending a line
/// while a turn runs is either guidance for that turn or a turn of its own, and
/// only the person pressing the key knows which they meant.
/// </remarks>
public sealed record SubmitRequest(string Input, string Submit, string Intent = "followup")
{
    /// <summary>
    /// The intent that queues the line behind whatever is running.
    /// </summary>
    /// <remarks>
    /// Spelled out again rather than used as the parameter's default: a primary
    /// constructor's default is evaluated outside this type, so it cannot name a
    /// member of it.
    /// </remarks>
    public const string IntentFollowUp = "followup";

    /// <summary>
    /// The intent that slips the line into the running turn.
    /// </summary>
    public const string IntentSteer = "steer";
}
