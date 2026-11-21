namespace Reasonix.Studio.Kernel.Client;

/// <summary>Connection details discovered by the kernel's provider probe.</summary>
/// <param name="Kind">Selected protocol.</param>
/// <param name="BaseUrl">Resolved endpoint.</param>
/// <param name="Models">Available model ids.</param>
/// <param name="Default">Suggested default model.</param>
/// <param name="Vision">Models that accept images.</param>
/// <param name="AuthHeader">Whether the endpoint uses an authorization header.</param>
/// <param name="NoProxy">Whether the endpoint answered directly.</param>
/// <param name="Kinds">Advertised compatible protocols for the model listing.</param>
/// <param name="Ambiguous">Whether discovery leaves the protocol to the user.</param>
public sealed record ProviderProbe(string Kind, string? BaseUrl, string[] Models, string? Default, string[]? Vision, bool AuthHeader, bool NoProxy, string[]? Kinds = null, bool Ambiguous = false);
