using Avalonia.Input;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Reasonix.Studio.Shell.Host;

/// <summary>Registers global shortcuts on the X server used by the native window.</summary>
internal static partial class X11Shortcut
{
    /// <summary>Reserves a key on its own X connection, leaving the window connection untouched.</summary>
    /// <param name="chord">The accelerator to reserve.</param>
    /// <param name="press">The callback for the initial key press.</param>
    /// <returns>A lease release operation, or null when the X server declines the request.</returns>
    internal static unsafe Action? Register(ShortcutChord chord, Action press)
    {
        nint connection;
        int screen;
        try
        {
            connection = Connect(0, out screen);
        }
        catch (DllNotFoundException)
        {
            return null;
        }

        if (connection == 0)
        {
            return null;
        }

        var transferred = false;
        try
        {
            if (ConnectionHasError(connection) != 0 || !TryReadBinding(connection, screen, chord, out var root, out var binding))
            {
                return null;
            }

            var requests = binding.Grabs().Select(mask => GrabKey(connection, 0, root, mask, binding.Keycode, 1, 1)).ToArray();
            foreach (var request in requests)
            {
                var error = RequestCheck(connection, request);
                if (error != 0)
                {
                    NativeMemory.Free((void*)error);
                    return null;
                }
            }

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            var holding = false;
            int? releasedAt = null;
            timer.Tick += (_, _) =>
            {
                while (timer.IsEnabled && PollForEvent(connection) is var ev && ev != 0)
                {
                    try
                    {
                        var type = Marshal.ReadByte(ev) & 0x7F;
                        if ((type is not (2 or 3)) || Marshal.ReadByte(ev, 1) != binding.Keycode)
                        {
                            continue;
                        }

                        var time = Marshal.ReadInt32(ev, 4);
                        if (type == 3)
                        {
                            // Core X11 sends repeat as a release and press with the same server time.
                            releasedAt = time;
                            holding = false;
                        }
                        else if (!holding && binding.Matches(unchecked((ushort)Marshal.ReadInt16(ev, 28))))
                        {
                            holding = true;
                            if (releasedAt != time)
                            {
                                press();
                            }
                        }
                    }
                    finally
                    {
                        NativeMemory.Free((void*)ev);
                    }
                }
            };

            timer.Start();
            transferred = true;
            return () =>
            {
                timer.Stop();
                Disconnect(connection);
            };
        }
        finally
        {
            if (!transferred)
            {
                // Every partial grab belongs to this connection and is released with it.
                Disconnect(connection);
            }
        }
    }

    /// <summary>Resolves the shortcut and lock masks from the current X server mapping.</summary>
    /// <param name="chord">The requested accelerator.</param>
    /// <param name="first">The first keycode in the keyboard map.</param>
    /// <param name="keysyms">The server's keysyms, grouped by keycode.</param>
    /// <param name="symbolsPerKey">The number of keysyms for each keycode.</param>
    /// <param name="modifierKeys">The keycodes under each of the eight modifier slots.</param>
    /// <param name="keysPerModifier">The number of keycodes for each slot.</param>
    /// <returns>The binding, or null when the key cannot be resolved.</returns>
    internal static Binding? CreateBinding(ShortcutChord chord, byte first, uint[] keysyms, int symbolsPerKey, byte[] modifierKeys, int keysPerModifier)
    {
        var symbol = KeySymbol(chord.Key);
        if (symbol == 0 || first == 0 || symbolsPerKey <= 0 || keysPerModifier <= 0 ||
            modifierKeys.Length < 8 * keysPerModifier || Array.IndexOf(keysyms, symbol) is not (>= 0 and var index))
        {
            return null;
        }

        var alt = FindModifiers(0xFFE9, 0xFFEA);
        var meta = FindModifiers(0xFFEB, 0xFFEC);
        if (meta == 0)
        {
            meta = FindModifiers(0xFFE7, 0xFFE8) & ~alt;
        }

        var modifiers = (chord.Modifiers.HasFlag(KeyModifiers.Shift) ? 1 : 0) |
            (chord.Modifiers.HasFlag(KeyModifiers.Control) ? 4 : 0) |
            (chord.Modifiers.HasFlag(KeyModifiers.Alt) ? ModifierSlot(alt, 8) : 0) |
            (chord.Modifiers.HasFlag(KeyModifiers.Meta) ? ModifierSlot(meta, 64) : 0);
        var locks = 2 | FindModifiers(0xFF7F, 0xFF14);
        return new Binding((byte)(first + (index / symbolsPerKey)), (ushort)modifiers, (ushort)(locks & ~modifiers));

        int FindModifiers(uint left, uint right)
        {
            var mask = 0;
            for (var slot = 0; slot < 8; ++slot)
            {
                for (var entry = 0; entry < keysPerModifier; ++entry)
                {
                    var code = modifierKeys[(slot * keysPerModifier) + entry];
                    var start = (code - first) * symbolsPerKey;
                    if (start < 0 || start + symbolsPerKey > keysyms.Length)
                    {
                        continue;
                    }

                    if (keysyms.AsSpan(start, symbolsPerKey).Contains(left) || keysyms.AsSpan(start, symbolsPerKey).Contains(right))
                    {
                        mask |= 1 << slot;
                    }
                }
            }

            return mask;
        }

        static int ModifierSlot(int mask, int fallback) => mask == 0 ? fallback : (mask & -mask);
    }

    /// <summary>Reads the root window and the keyboard mapping for one X display.</summary>
    /// <param name="connection">The X connection.</param>
    /// <param name="screen">The selected display screen.</param>
    /// <param name="chord">The requested accelerator.</param>
    /// <param name="root">The selected screen's root window.</param>
    /// <param name="binding">The keycode and modifier masks to reserve.</param>
    /// <returns>Whether the binding was resolved.</returns>
    private static unsafe bool TryReadBinding(nint connection, int screen, ShortcutChord chord, out uint root, out Binding binding)
    {
        root = 0;
        binding = default;
        var setup = GetSetup(connection);
        var iterator = RootsIterator(setup);
        for (var i = 0; (i < screen) && (iterator.Remaining > 0); i++)
        {
            NextScreen(ref iterator);
        }

        if (iterator.Remaining == 0)
        {
            return false;
        }

        root = unchecked((uint)Marshal.ReadInt32(iterator.Data));

        // These two bytes are min_keycode and max_keycode in the X11 setup reply.
        var first = Marshal.ReadByte(setup, 34);
        var last = Marshal.ReadByte(setup, 35);
        if (first == 0 || last < first)
        {
            return false;
        }

        var keymap = KeyboardMappingReply(connection, GetKeyboardMapping(connection, first, (byte)(last - first + 1)), 0);
        var modmap = ModifierMappingReply(connection, GetModifierMapping(connection), 0);
        try
        {
            if (keymap == 0 || modmap == 0)
            {
                return false;
            }

            var keys = new ReadOnlySpan<uint>((void*)MappingKeysyms(keymap), MappingKeysymsLength(keymap)).ToArray();
            var mods = new ReadOnlySpan<byte>((void*)ModifierKeycodes(modmap), ModifierKeycodesLength(modmap)).ToArray();
            if (CreateBinding(chord, first, keys, Marshal.ReadByte(keymap, 1), mods, Marshal.ReadByte(modmap, 1)) is not { } resolved)
            {
                return false;
            }

            binding = resolved;
            return true;
        }
        finally
        {
            NativeMemory.Free((void*)keymap);
            NativeMemory.Free((void*)modmap);
        }
    }

    /// <summary>Maps the supported accelerator keys to X11 keysyms.</summary>
    /// <param name="key">The non-modifier key.</param>
    /// <returns>The X11 symbol, or zero when unavailable.</returns>
    private static uint KeySymbol(PhysicalKey key) => key switch
    {
        >= PhysicalKey.A and <= PhysicalKey.Z => (uint)('a' + (key - PhysicalKey.A)),
        >= PhysicalKey.Digit0 and <= PhysicalKey.Digit9 => (uint)('0' + (key - PhysicalKey.Digit0)),
        >= PhysicalKey.F1 and <= PhysicalKey.F24 => (uint)(0xFFBE + (key - PhysicalKey.F1)),
        PhysicalKey.Backquote => 0x60,
        PhysicalKey.Backslash => 0x5C,
        PhysicalKey.BracketLeft => 0x5B,
        PhysicalKey.BracketRight => 0x5D,
        PhysicalKey.Comma => 0x2C,
        PhysicalKey.Equal => 0x3D,
        PhysicalKey.Minus => 0x2D,
        PhysicalKey.Period => 0x2E,
        PhysicalKey.Quote => 0x27,
        PhysicalKey.Semicolon => 0x3B,
        PhysicalKey.Slash => 0x2F,
        PhysicalKey.Space => 0x20,
        PhysicalKey.Tab => 0xFF09,
        PhysicalKey.Enter => 0xFF0D,
        PhysicalKey.Escape => 0xFF1B,
        PhysicalKey.Backspace => 0xFF08,
        PhysicalKey.Delete => 0xFFFF,
        PhysicalKey.Insert => 0xFF63,
        PhysicalKey.Home => 0xFF50,
        PhysicalKey.End => 0xFF57,
        PhysicalKey.PageUp => 0xFF55,
        PhysicalKey.PageDown => 0xFF56,
        PhysicalKey.ArrowLeft => 0xFF51,
        PhysicalKey.ArrowUp => 0xFF52,
        PhysicalKey.ArrowRight => 0xFF53,
        PhysicalKey.ArrowDown => 0xFF54,
        _ => 0,
    };

    /// <summary>An X11 accelerator's keycode and its active and ignored modifiers.</summary>
    /// <param name="Keycode">The X keycode.</param>
    /// <param name="Modifiers">The requested modifier slots.</param>
    /// <param name="IgnoredLocks">The lock slots to ignore.</param>
    internal readonly record struct Binding(byte Keycode, ushort Modifiers, ushort IgnoredLocks)
    {
        /// <summary>Enumerates every exact mask the server must reserve for the accelerator.</summary>
        /// <returns>The requested modifier slots combined with each lock state.</returns>
        internal IEnumerable<ushort> Grabs()
        {
            var locks = (int)IgnoredLocks;
            for (var active = locks; ; active = (active - 1) & locks)
            {
                yield return (ushort)(Modifiers | active);
                if (active == 0)
                {
                    yield break;
                }
            }
        }

        /// <summary>Checks event modifiers while ignoring lock slots and mouse-button bits.</summary>
        /// <param name="state">The X11 key event's modifier state.</param>
        /// <returns>Whether the active modifiers match the shortcut.</returns>
        internal bool Matches(ushort state) => (state & 0xFF & ~IgnoredLocks) == Modifiers;
    }
}
