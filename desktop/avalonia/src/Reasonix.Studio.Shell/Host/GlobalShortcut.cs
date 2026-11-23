using System;
using System.IO;

namespace Reasonix.Studio.Shell.Host;

/// <summary>Owns the device-local accelerator and keeps the current registration until a replacement is saved.</summary>
internal sealed class GlobalShortcut : IDisposable
{
    /// <summary>The platform registrar.</summary>
    private readonly Func<ShortcutChord, Action, Action?> _register;

    /// <summary>Writes the device-local binding.</summary>
    private readonly Action<string> _save;

    /// <summary>Toggles the owning desktop window.</summary>
    private readonly Action _press;

    /// <summary>The accepted key combination.</summary>
    private ShortcutChord _active;

    /// <summary>Releases the current system registration.</summary>
    private Action? _release;

    /// <summary>Whether the shortcut can no longer be applied.</summary>
    private bool _closed;

    /// <summary>Initializes a new instance of the <see cref="GlobalShortcut"/> class.</summary>
    /// <param name="saved">The prior accelerator.</param>
    /// <param name="register">The system registrar.</param>
    /// <param name="save">Persists the accelerator.</param>
    /// <param name="press">Shows or hides the window.</param>
    internal GlobalShortcut(string saved, Func<ShortcutChord, Action, Action?> register, Action<string> save, Action press)
    {
        _register = register;
        _save = save;
        _press = press;
        Status = Update(saved, persist: false);
        Binding = saved;
    }

    /// <summary>Gets the last accepted binding or the saved binding awaiting registration.</summary>
    internal string Binding { get; private set; } = string.Empty;

    /// <summary>Gets the state independent of a failed edit.</summary>
    internal GlobalShortcutStatus Status
    {
        get; private set;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _closed = true;
        var release = _release;
        _release = null;
        release?.Invoke();
    }

    /// <summary>Applies an edit without replacing an active accelerator on refusal.</summary>
    /// <param name="text">The draft accelerator, or empty to disable.</param>
    /// <returns>The outcome of this edit.</returns>
    internal GlobalShortcutStatus Apply(string text) => Update(text, persist: true);

    /// <summary>Resolves, registers and persists the next accelerator before releasing the current one.</summary>
    /// <param name="text">The proposed accelerator.</param>
    /// <param name="persist">Whether this edit should be written to disk.</param>
    /// <returns>The result of this registration.</returns>
    private GlobalShortcutStatus Update(string text, bool persist)
    {
        if (_closed)
        {
            return GlobalShortcutStatus.Quitting;
        }

        var value = text.Trim();
        var next = default(ShortcutChord);
        Action? release = null;
        if (value.Length > 0)
        {
            if (!ShortcutChord.TryParse(value, OperatingSystem.IsMacOS(), out next))
            {
                return GlobalShortcutStatus.Invalid;
            }

            if (_release is not null && next == _active)
            {
                return GlobalShortcutStatus.Registered;
            }

            release = _register(next, () =>
            {
                if (!_closed && _active == next && _release is not null)
                {
                    _press();
                }
            });
            if (release is null)
            {
                return GlobalShortcutStatus.Unavailable;
            }
        }

        try
        {
            if (persist)
            {
                _save(value);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            release?.Invoke();
            return GlobalShortcutStatus.SaveFailed;
        }

        _release?.Invoke();
        _release = release;
        _active = next;
        Binding = value;
        return Status = release is null ? GlobalShortcutStatus.Disabled : GlobalShortcutStatus.Registered;
    }
}
