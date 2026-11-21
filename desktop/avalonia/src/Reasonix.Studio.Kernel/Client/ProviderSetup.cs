namespace Reasonix.Studio.Kernel.Client;

/// <summary>A provider's current onboarding state.</summary>
/// <param name="Required">Whether setup is outstanding.</param>
/// <param name="NoProvider">Whether no provider exists yet.</param>
/// <param name="Provider">Provider name.</param>
/// <param name="Model">Selected model.</param>
/// <param name="Error">Reason activation is unavailable.</param>
public sealed record ProviderSetup(
    bool Required,
    bool NoProvider = false,
    string? Provider = null,
    string? Model = null,
    string? Error = null);
