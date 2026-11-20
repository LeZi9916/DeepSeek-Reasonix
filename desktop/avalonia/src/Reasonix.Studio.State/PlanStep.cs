namespace Reasonix.Studio.State;

/// <summary>
/// Where one step of the kernel's task list stands.
/// </summary>
public enum TodoStatus
{
    /// <summary>
    /// The step has not started.
    /// </summary>
    Pending,

    /// <summary>
    /// The step is the one in hand.
    /// </summary>
    InProgress,

    /// <summary>
    /// The step is done.
    /// </summary>
    Completed,
}

/// <summary>
/// One step of the kernel's task list.
/// </summary>
public sealed record PlanStep
{
    /// <summary>
    /// Gets the step's text.
    /// </summary>
    public required string Text
    {
        get; init;
    }

    /// <summary>
    /// Gets where the step stands.
    /// </summary>
    public required TodoStatus Status
    {
        get; init;
    }

    /// <summary>
    /// Gets the wording the kernel keeps for while this is the step in hand.
    /// </summary>
    public string? ActiveForm
    {
        get; init;
    }

    /// <summary>
    /// Gets the step's depth in the kernel's list; sub-steps read as peers without it.
    /// </summary>
    public int? Level
    {
        get; init;
    }
}
