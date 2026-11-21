namespace Reasonix.Studio.Kernel.Client;

/// <summary>One model from the kernel's chat catalog.</summary>
/// <param name="Ref">Stable model reference.</param>
/// <param name="Model">Model name.</param>
/// <param name="DisplayName">Provider display name.</param>
/// <param name="ModelAlias">User's model label.</param>
/// <param name="Active">Whether currently selected.</param>
/// <param name="NotAdded">Whether excluded from the configured access list.</param>
/// <param name="Default">Whether selected for future sessions.</param>
/// <param name="Efforts">Supported reasoning levels.</param>
/// <param name="Effort">Effective level for this model.</param>
public sealed record ModelChoice(
    string Ref,
    string Model,
    string? DisplayName = null,
    string? ModelAlias = null,
    bool Active = false,
    bool NotAdded = false,
    bool Default = false,
    string[]? Efforts = null,
    string? Effort = null);
