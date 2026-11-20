namespace Reasonix.Studio.State;

/// <summary>
/// Money this session was billed, in one currency.
/// </summary>
/// <remarks>
/// Two rounds billed in different currencies stay two entries: adding them would
/// need a rate nobody quoted, and the sum would be a number with no unit.
/// </remarks>
/// <param name="Currency">The currency the amount is in.</param>
/// <param name="Amount">How much was billed, in that currency.</param>
public sealed record CurrencyAmount(string Currency, double Amount);
