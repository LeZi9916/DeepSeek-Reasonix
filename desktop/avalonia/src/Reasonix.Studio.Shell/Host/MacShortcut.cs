using Avalonia.Input;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Reasonix.Studio.Shell.Host;

/// <summary>Registers regular macOS shortcuts through the application event target.</summary>
internal static partial class MacShortcut
{
    /// <summary>The system event framework.</summary>
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

    /// <summary>The Carbon keyboard event class.</summary>
    private const uint Keyboard = 0x6B657962;

    /// <summary>The Carbon hotkey press event.</summary>
    private const uint Pressed = 5;

    /// <summary>The Carbon hotkey release event.</summary>
    private const uint Released = 6;

    /// <summary>The Studio registration signature.</summary>
    private const uint Signature = 0x52786E78;

    /// <summary>Passes an unrelated event to the next handler.</summary>
    private const int EventNotHandled = -9874;

    /// <summary>The HIToolbox ANSI keycodes in alphabetic order.</summary>
    private static readonly uint[] s_letters =
        [0x00, 0x0B, 0x08, 0x02, 0x0E, 0x03, 0x05, 0x04, 0x22, 0x26, 0x28, 0x25, 0x2E, 0x2D, 0x1F, 0x23, 0x0C, 0x0F, 0x01, 0x11, 0x20, 0x09, 0x0D, 0x07, 0x10, 0x06];

    /// <summary>The HIToolbox ANSI keycodes ordered from zero to nine.</summary>
    private static readonly uint[] s_digits = [0x1D, 0x12, 0x13, 0x14, 0x15, 0x17, 0x16, 0x1A, 0x1C, 0x19];

    /// <summary>The HIToolbox function keycodes from F1 to F20.</summary>
    private static readonly uint[] s_functions =
        [0x7A, 0x78, 0x63, 0x76, 0x60, 0x61, 0x62, 0x64, 0x65, 0x6D, 0x67, 0x6F, 0x69, 0x6B, 0x71, 0x6A, 0x40, 0x4F, 0x50, 0x5A];

    /// <summary>The next hotkey identifier in this process.</summary>
    private static int s_nextId;

    /// <summary>Dispatches one Carbon event.</summary>
    /// <param name="next">The next handler.</param><param name="ev">The native event.</param><param name="data">The callback context.</param>
    /// <returns>The event status.</returns>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Callback(nint next, nint ev, nint data);

    /// <summary>Reserves the accelerator and routes only that hotkey's press and release.</summary>
    /// <param name="chord">The accelerator to reserve.</param>
    /// <param name="press">The action to run once per press.</param>
    /// <returns>The operation to release this registration, or null when declined.</returns>
    internal static unsafe Action? Register(ShortcutChord chord, Action press)
    {
        var key = Code(chord.Key);
        if (key is null)
        {
            return null;
        }

        var flags = (chord.Modifiers.HasFlag(KeyModifiers.Meta) ? 1u << 8 : 0) |
            (chord.Modifiers.HasFlag(KeyModifiers.Shift) ? 1u << 9 : 0) |
            (chord.Modifiers.HasFlag(KeyModifiers.Alt) ? 1u << 11 : 0) |
            (chord.Modifiers.HasFlag(KeyModifiers.Control) ? 1u << 12 : 0);
        var target = GetApplicationEventTarget();
        var id = new HotKeyId(Signature, unchecked((uint)Interlocked.Increment(ref s_nextId)));
        if (RegisterEventHotKey(key.Value, flags, id, target, 1, out var hotkey) != 0)
        {
            return null;
        }

        var holding = false;
        Callback callback = (_, ev, _) =>
        {
            if (GetEventParameter(ev, 0x2D2D2D2D, 0x686B6964, 0, 8, 0, out var received) != 0 ||
                received != id)
            {
                return EventNotHandled;
            }

            if (GetEventKind(ev) == Released)
            {
                holding = false;
            }
            else if (!holding)
            {
                holding = true;
                press();
            }

            return 0;
        };
        var types = stackalloc EventType[2] { new(Keyboard, Pressed), new(Keyboard, Released) };
        if (InstallEventHandler(target, Marshal.GetFunctionPointerForDelegate(callback), 2, types, 0, out var handler) != 0)
        {
            UnregisterEventHotKey(hotkey);
            return null;
        }

        return () =>
        {
            UnregisterEventHotKey(hotkey);
            RemoveEventHandler(handler);
            GC.KeepAlive(callback);
        };
    }

    /// <summary>Looks up Carbon's ANSI position or an independent keycode.</summary>
    /// <param name="key">The key position.</param>
    /// <returns>The keycode, when published for this key.</returns>
    private static uint? Code(PhysicalKey key) => key switch
    {
        >= PhysicalKey.A and <= PhysicalKey.Z => s_letters[key - PhysicalKey.A],
        >= PhysicalKey.Digit0 and <= PhysicalKey.Digit9 => s_digits[key - PhysicalKey.Digit0],
        >= PhysicalKey.F1 and <= PhysicalKey.F20 => s_functions[key - PhysicalKey.F1],
        PhysicalKey.Backquote => 0x32,
        PhysicalKey.Backslash => 0x2A,
        PhysicalKey.BracketLeft => 0x21,
        PhysicalKey.BracketRight => 0x1E,
        PhysicalKey.Comma => 0x2B,
        PhysicalKey.Equal => 0x18,
        PhysicalKey.Minus => 0x1B,
        PhysicalKey.Period => 0x2F,
        PhysicalKey.Quote => 0x27,
        PhysicalKey.Semicolon => 0x29,
        PhysicalKey.Slash => 0x2C,
        PhysicalKey.Space => 0x31,
        PhysicalKey.Tab => 0x30,
        PhysicalKey.Enter => 0x24,
        PhysicalKey.Escape => 0x35,
        PhysicalKey.Backspace => 0x33,
        PhysicalKey.Delete => 0x75,
        PhysicalKey.Insert => 0x72,
        PhysicalKey.Home => 0x73,
        PhysicalKey.End => 0x77,
        PhysicalKey.PageUp => 0x74,
        PhysicalKey.PageDown => 0x79,
        PhysicalKey.ArrowLeft => 0x7B,
        PhysicalKey.ArrowRight => 0x7C,
        PhysicalKey.ArrowDown => 0x7D,
        PhysicalKey.ArrowUp => 0x7E,
        _ => null,
    };

    /// <summary>Resolves the application's event target.</summary>
    /// <returns>The application event target.</returns>
    [LibraryImport(Carbon)]
    private static partial nint GetApplicationEventTarget();

    /// <summary>Reserves the native hotkey.</summary>
    [LibraryImport(Carbon)]
    private static partial int RegisterEventHotKey(uint key, uint modifiers, HotKeyId id, nint target, uint options, out nint hotkey);

    /// <summary>Releases a reserved hotkey.</summary>
    /// <param name="hotkey">The registered hotkey.</param>
    /// <returns>The OS status.</returns>
    [LibraryImport(Carbon)]
    private static partial int UnregisterEventHotKey(nint hotkey);

    /// <summary>Installs the keyboard callback.</summary>
    [LibraryImport(Carbon)]
    private static unsafe partial int InstallEventHandler(nint target, nint callback, uint count, EventType* types, nint data, out nint handler);

    /// <summary>Removes the callback from the event target.</summary>
    /// <param name="handler">The installed callback.</param>
    /// <returns>The OS status.</returns>
    [LibraryImport(Carbon)]
    private static partial int RemoveEventHandler(nint handler);

    /// <summary>Reads the kind of native event.</summary>
    /// <param name="ev">The Carbon event.</param>
    /// <returns>The event kind.</returns>
    [LibraryImport(Carbon)]
    private static partial uint GetEventKind(nint ev);

    /// <summary>Reads the registered identifier from the event.</summary>
    [LibraryImport(Carbon)]
    private static partial int GetEventParameter(nint ev, uint name, uint type, nint actualType, uint size, nint actualSize, out HotKeyId result);

    /// <summary>The native event class and kind.</summary>
    /// <param name="Class">The Carbon event class.</param><param name="Kind">The event kind.</param>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct EventType(uint Class, uint Kind);

    /// <summary>The identifiers carried by the native hotkey event.</summary>
    /// <param name="Signature">The application tag.</param><param name="Id">The registration number.</param>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct HotKeyId(uint Signature, uint Id);
}
