namespace Reasonix.Studio.Shell.Settings;

/// <summary>Presentation of one stage in the setup progress row.</summary>
/// <param name="Number">The reader-facing stage number.</param>
/// <param name="Label">The localized stage label.</param>
/// <param name="Current">Whether this stage is active.</param>
/// <param name="Complete">Whether this stage has finished.</param>
/// <param name="HasDivider">Whether this stage follows another one.</param>
internal sealed record OnboardingStep(int Number, string Label, bool Current, bool Complete, bool HasDivider);
