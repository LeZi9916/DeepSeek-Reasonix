namespace Reasonix.Studio.Kernel.Client;

/// <summary>One model from the kernel's chat catalog.</summary>
/// <param name="Ref">Stable model reference.</param>
/// <param name="Model">Model name.</param>
/// <param name="Provider">Configuration entry that owns the route.</param>
/// <param name="DisplayName">Provider display name.</param>
/// <param name="ModelAlias">User's model label.</param>
/// <param name="ModelShort">Model name without its entry's shared prefix.</param>
/// <param name="Kind">Protocol this route answers on.</param>
/// <param name="Answers">What this route produces, normally <c>chat</c>.</param>
/// <param name="Active">Whether currently selected.</param>
/// <param name="NotAdded">Whether excluded from the configured access list.</param>
/// <param name="Default">Whether selected for future sessions.</param>
/// <param name="Vision">Whether the model accepts image input.</param>
/// <param name="Mark">Vendor slug of the preset the provider came from.</param>
/// <param name="ServiceMark">Vendor slug of the service, including a relay's upstream.</param>
/// <param name="Vendor">Endpoint host shared by one account's routes.</param>
/// <param name="KeyEnv">Credential slot that names the account beside the host.</param>
/// <param name="Preset">Whether the provider name is one the product shipped.</param>
/// <param name="ForcesThinking">Whether even the cheapest level still reasons.</param>
/// <param name="Efforts">Supported reasoning levels.</param>
/// <param name="Effort">Effective level for this model.</param>
/// <param name="ContextWindow">Context window the route declares.</param>
public sealed record ModelChoice(
    string Ref,
    string Model,
    string Provider = "",
    string? DisplayName = null,
    string? ModelAlias = null,
    string? ModelShort = null,
    string? Kind = null,
    string? Answers = null,
    bool Active = false,
    bool NotAdded = false,
    bool Default = false,
    bool Vision = false,
    string? Mark = null,
    string? ServiceMark = null,
    string? Vendor = null,
    string? KeyEnv = null,
    bool Preset = false,
    bool ForcesThinking = false,
    string[]? Efforts = null,
    string? Effort = null,
    int ContextWindow = 0);
