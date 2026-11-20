namespace Reasonix.Studio.Shell.Host;

/// <summary>
/// Where the shell's own process stood when it answered.
/// </summary>
public enum GlobalShortcutStatus
{
    /// <summary>
    /// The person turned the shortcut off.
    /// </summary>
    Disabled,

    /// <summary>
    /// The platform took the binding.
    /// </summary>
    Registered,

    /// <summary>
    /// The binding is not a key combination this shell can parse.
    /// </summary>
    Invalid,

    /// <summary>
    /// Another application already holds the binding.
    /// </summary>
    Unavailable,

    /// <summary>
    /// This shell has no global shortcut surface at all.
    /// </summary>
    Unsupported,
}

/// <summary>
/// The global shortcut as the shell currently holds it.
/// </summary>
/// <param name="Binding">The shortcut, empty when none is set.</param>
/// <param name="Status">What happened to the binding.</param>
public sealed record GlobalShortcutInfo(string Binding, GlobalShortcutStatus Status)
{
    /// <summary>
    /// Gets the answer a shell with no shortcut surface gives.
    /// </summary>
    public static GlobalShortcutInfo Unsupported { get; } = new GlobalShortcutInfo(string.Empty, GlobalShortcutStatus.Unsupported);
}
