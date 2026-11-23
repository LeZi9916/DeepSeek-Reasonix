using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Shell.Host;

/// <summary>A desktop accelerator resolved to one key and its modifiers.</summary>
/// <param name="Key">The position of the non-modifier key.</param>
/// <param name="Modifiers">The modifiers held with the key.</param>
internal readonly record struct ShortcutChord(PhysicalKey Key, KeyModifiers Modifiers)
{
    /// <summary>The desktop shell's limit for a typed accelerator.</summary>
    internal const int MaxLength = 128;

    /// <summary>Recognizes punctuation by the same names used by the desktop renderer.</summary>
    private static readonly IReadOnlyDictionary<PhysicalKey, string> s_keys = new Dictionary<PhysicalKey, string>
    {
        [PhysicalKey.Backquote] = "`",
        [PhysicalKey.Backslash] = "\\",
        [PhysicalKey.BracketLeft] = "[",
        [PhysicalKey.BracketRight] = "]",
        [PhysicalKey.Comma] = ",",
        [PhysicalKey.Equal] = "=",
        [PhysicalKey.Minus] = "-",
        [PhysicalKey.Period] = ".",
        [PhysicalKey.Quote] = "'",
        [PhysicalKey.Semicolon] = ";",
        [PhysicalKey.Slash] = "/",
        [PhysicalKey.ArrowDown] = "Down",
        [PhysicalKey.ArrowLeft] = "Left",
        [PhysicalKey.ArrowRight] = "Right",
        [PhysicalKey.ArrowUp] = "Up",
    };

    /// <summary>Maps the shell's typed accelerator form to a physical chord.</summary>
    /// <param name="text">The accelerator.</param>
    /// <param name="mac">Whether CommandOrControl means Command.</param>
    /// <param name="chord">The resolved chord.</param>
    /// <returns>Whether the accelerator can be used.</returns>
    internal static bool TryParse(string text, bool mac, out ShortcutChord chord)
    {
        chord = default;
        if (text.Length > MaxLength)
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var mods = KeyModifiers.None;
        foreach (var part in parts.Take(parts.Length - 1))
        {
            var modifier = part.ToUpperInvariant() switch
            {
                "COMMANDORCONTROL" or "CMDORCTRL" => mac ? KeyModifiers.Meta : KeyModifiers.Control,
                "CONTROL" or "CTRL" => KeyModifiers.Control,
                "COMMAND" or "CMD" or "META" or "SUPER" => KeyModifiers.Meta,
                "ALT" or "OPTION" => KeyModifiers.Alt,
                "SHIFT" => KeyModifiers.Shift,
                _ => KeyModifiers.None,
            };
            if (modifier == KeyModifiers.None || mods.HasFlag(modifier))
            {
                return false;
            }

            mods |= modifier;
        }

        var name = parts[^1];
        var key = s_keys.FirstOrDefault(pair => string.Equals(pair.Value, name, StringComparison.OrdinalIgnoreCase)).Key;
        if (key == PhysicalKey.None)
        {
            if (name.Equals("Plus", StringComparison.OrdinalIgnoreCase))
            {
                name = nameof(PhysicalKey.Equal);
                mods |= KeyModifiers.Shift;
            }
            else if (name.Length == 1 && char.IsAsciiDigit(name[0]))
            {
                name = "Digit" + name;
            }
            else if (name.Equals("Return", StringComparison.OrdinalIgnoreCase))
            {
                name = nameof(PhysicalKey.Enter);
            }
            else if (name.Equals("Esc", StringComparison.OrdinalIgnoreCase))
            {
                name = nameof(PhysicalKey.Escape);
            }

            if (!Enum.TryParse(name, ignoreCase: true, out key) || !char.IsAsciiLetter(name[0]) || !name.All(char.IsAsciiLetterOrDigit))
            {
                return false;
            }
        }

        chord = new ShortcutChord(key, mods);
        return chord.IsBindable();
    }

    /// <summary>Validates recorded keys and requires at least one non-Shift modifier.</summary>
    /// <returns>Whether the combination can be offered to the operating system.</returns>
    internal bool IsBindable() => (Modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0 &&
        ((Key is (>= PhysicalKey.A and <= PhysicalKey.Z) or (>= PhysicalKey.Digit0 and <= PhysicalKey.Digit9) or
            (>= PhysicalKey.F1 and <= PhysicalKey.F24) or PhysicalKey.Backspace or PhysicalKey.Delete or PhysicalKey.Enter or
            PhysicalKey.Escape or PhysicalKey.End or PhysicalKey.Home or PhysicalKey.Insert or PhysicalKey.PageDown or
            PhysicalKey.PageUp or PhysicalKey.Space or PhysicalKey.Tab) || s_keys.ContainsKey(Key));

    /// <summary>Spells a recorded chord as a typed accelerator.</summary>
    /// <returns>The recorded key combination.</returns>
    internal string Format()
    {
        var modifiers = Modifiers;
        var parts = new List<string>(5);
        Add(KeyModifiers.Control, "Control");
        Add(KeyModifiers.Alt, "Alt");
        Add(KeyModifiers.Shift, "Shift");
        Add(KeyModifiers.Meta, "Super");
        var key = Key.ToString();
        parts.Add(s_keys.GetValueOrDefault(Key) ?? (key.StartsWith("Digit", StringComparison.Ordinal) ? key[5..] : key));
        return string.Join("+", parts);

        void Add(KeyModifiers bit, string word)
        {
            if (modifiers.HasFlag(bit))
            {
                parts.Add(word);
            }
        }
    }
}
