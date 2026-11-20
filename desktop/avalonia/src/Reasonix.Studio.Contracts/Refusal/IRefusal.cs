namespace Reasonix.Studio.Contracts.Refusal;

/// <summary>
/// What a refused request carries across the boundary, whatever transport
/// produced it.
/// </summary>
/// <remarks>
/// The client throws a refusal, the page turns it into a sentence, and neither
/// should have to know the other's type. This is the shared shape they agree on:
/// a status, a coded reason when the kernel sent one, and the plain-text account
/// when it did not. It lives beside <see cref="Reason"/> because it is the same
/// contract seen from the other side.
/// </remarks>
public interface IRefusal
{
    /// <summary>
    /// Gets the HTTP status the kernel answered with.
    /// </summary>
    /// <remarks>
    /// Not nullable: a refusal exists because an answer arrived, and an answer
    /// that never arrived is a transport failure with no refusal in it at all.
    /// </remarks>
    int Status
    {
        get;
    }

    /// <summary>
    /// Gets the coded refusal, or null when the answer carried none.
    /// </summary>
    Reason? Reason
    {
        get;
    }

    /// <summary>
    /// Gets the kernel's plain-text account, or null when the answer was coded
    /// or carried nothing.
    /// </summary>
    string? Detail
    {
        get;
    }

    /// <summary>
    /// Gets the dotted code, or null when this refusal carries none.
    /// </summary>
    string? Code
    {
        get;
    }
}
