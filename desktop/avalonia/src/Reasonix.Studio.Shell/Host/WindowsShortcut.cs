using Avalonia.Controls;
using Avalonia.Input;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Reasonix.Studio.Shell.Host;

/// <summary>Reserves Win32 accelerators against the desktop window's message queue.</summary>
internal static partial class WindowsShortcut
{
    /// <summary>The next native window message identifier.</summary>
    private static int s_nextId;

    /// <summary>Reserves a system shortcut and listens for its window message.</summary>
    /// <param name="window">The window that receives the registered message.</param>
    /// <param name="chord">The accelerator to reserve.</param>
    /// <param name="press">The action for an accepted hotkey message.</param>
    /// <returns>The operation to release the system registration, or null when declined.</returns>
    internal static Action? Register(Window window, ShortcutChord chord, Action press)
    {
        var key = Code(chord.Key);
        if (key == 0 || window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } handle)
        {
            return null;
        }

        var flags = 0x4000u |
            (chord.Modifiers.HasFlag(KeyModifiers.Alt) ? 1u : 0) |
            (chord.Modifiers.HasFlag(KeyModifiers.Control) ? 2u : 0) |
            (chord.Modifiers.HasFlag(KeyModifiers.Shift) ? 4u : 0) |
            (chord.Modifiers.HasFlag(KeyModifiers.Meta) ? 8u : 0);
        var id = Interlocked.Increment(ref s_nextId) & 0x7FFF;
        if (!RegisterHotKey(handle.Handle, id, flags, key))
        {
            return null;
        }

        Win32Properties.AddWndProcHookCallback(window, Hook);
        return () =>
        {
            UnregisterHotKey(handle.Handle, id);
            Win32Properties.RemoveWndProcHookCallback(window, Hook);
        };

        nint Hook(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled)
        {
            if (message == 0x0312 && wParam == id)
            {
                handled = true;
                press();
            }

            return 0;
        }
    }

    /// <summary>Looks up the Win32 virtual-key code for a supported accelerator.</summary>
    /// <param name="key">The key position.</param>
    /// <returns>The corresponding virtual-key code.</returns>
    private static uint Code(PhysicalKey key) => key switch
    {
        >= PhysicalKey.A and <= PhysicalKey.Z => (uint)(0x41 + (key - PhysicalKey.A)),
        >= PhysicalKey.Digit0 and <= PhysicalKey.Digit9 => (uint)(0x30 + (key - PhysicalKey.Digit0)),
        >= PhysicalKey.F1 and <= PhysicalKey.F24 => (uint)(0x70 + (key - PhysicalKey.F1)),
        PhysicalKey.Backquote => 0xC0,
        PhysicalKey.Backslash => 0xDC,
        PhysicalKey.BracketLeft => 0xDB,
        PhysicalKey.BracketRight => 0xDD,
        PhysicalKey.Comma => 0xBC,
        PhysicalKey.Equal => 0xBB,
        PhysicalKey.Minus => 0xBD,
        PhysicalKey.Period => 0xBE,
        PhysicalKey.Quote => 0xDE,
        PhysicalKey.Semicolon => 0xBA,
        PhysicalKey.Slash => 0xBF,
        PhysicalKey.Space => 0x20,
        PhysicalKey.Tab => 0x09,
        PhysicalKey.Enter => 0x0D,
        PhysicalKey.Escape => 0x1B,
        PhysicalKey.Backspace => 0x08,
        PhysicalKey.Delete => 0x2E,
        PhysicalKey.Insert => 0x2D,
        PhysicalKey.Home => 0x24,
        PhysicalKey.End => 0x23,
        PhysicalKey.PageUp => 0x21,
        PhysicalKey.PageDown => 0x22,
        PhysicalKey.ArrowLeft => 0x25,
        PhysicalKey.ArrowUp => 0x26,
        PhysicalKey.ArrowRight => 0x27,
        PhysicalKey.ArrowDown => 0x28,
        _ => 0,
    };

    /// <summary>Reserves a key combination with the system.</summary>
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint window, int id, uint modifiers, uint key);

    /// <summary>Releases a system registration.</summary>
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint window, int id);
}
