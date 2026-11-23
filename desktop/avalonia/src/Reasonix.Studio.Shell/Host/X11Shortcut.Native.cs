using System.Runtime.InteropServices;

namespace Reasonix.Studio.Shell.Host;

internal static partial class X11Shortcut
{
    /// <summary>The system's X11 protocol library.</summary>
    private const string Library = "libxcb.so.1";

    /// <summary>Opens an independent XCB connection.</summary>
    /// <param name="display">The display name, or null for the default.</param>
    /// <param name="screen">Receives the default screen.</param>
    /// <returns>The native connection.</returns>
    [LibraryImport(Library, EntryPoint = "xcb_connect")]
    private static partial nint Connect(nint display, out int screen);

    /// <summary>Checks for a transport or setup error.</summary>
    /// <param name="connection">The XCB connection.</param>
    /// <returns>Zero when the connection is usable.</returns>
    [LibraryImport(Library, EntryPoint = "xcb_connection_has_error")]
    private static partial int ConnectionHasError(nint connection);

    /// <summary>Closes the connection and releases its grabs.</summary>
    /// <param name="connection">The XCB connection.</param>
    [LibraryImport(Library, EntryPoint = "xcb_disconnect")]
    private static partial void Disconnect(nint connection);

    /// <summary>Reads the connection setup.</summary>
    /// <param name="connection">The XCB connection.</param>
    /// <returns>The server's setup reply.</returns>
    [LibraryImport(Library, EntryPoint = "xcb_get_setup")]
    private static partial nint GetSetup(nint connection);

    /// <summary>Enumerates the screens described by the server.</summary>
    /// <param name="setup">The server setup.</param>
    /// <returns>The native iterator.</returns>
    [LibraryImport(Library, EntryPoint = "xcb_setup_roots_iterator")]
    private static partial ScreenIterator RootsIterator(nint setup);

    /// <summary>Advances to the next screen.</summary>
    /// <param name="iterator">The native screen iterator.</param>
    [LibraryImport(Library, EntryPoint = "xcb_screen_next")]
    private static partial void NextScreen(ref ScreenIterator iterator);

    /// <summary>Requests the server's keysym map.</summary>
    /// <param name="connection">The XCB connection.</param>
    /// <param name="first">The first requested keycode.</param>
    /// <param name="count">The number of keycodes.</param>
    /// <returns>The request cookie.</returns>
    [LibraryImport(Library, EntryPoint = "xcb_get_keyboard_mapping")]
    private static partial Cookie GetKeyboardMapping(nint connection, byte first, byte count);

    /// <summary>Receives the keyboard mapping.</summary>
    /// <param name="connection">The XCB connection.</param>
    /// <param name="cookie">The request cookie.</param>
    /// <param name="error">Optional native error output.</param>
    /// <returns>The allocated reply, or null on error.</returns>
    [LibraryImport(Library, EntryPoint = "xcb_get_keyboard_mapping_reply")]
    private static partial nint KeyboardMappingReply(nint connection, Cookie cookie, nint error);

    /// <summary>Gets the keysyms from a keyboard mapping.</summary>
    /// <param name="reply">The native reply.</param>
    /// <returns>The start of the array.</returns>
    [LibraryImport(Library, EntryPoint = "xcb_get_keyboard_mapping_keysyms")]
    private static partial nint MappingKeysyms(nint reply);

    /// <summary>Measures the keysym array.</summary>
    /// <param name="reply">The native reply.</param>
    /// <returns>The number of keysyms.</returns>
    [LibraryImport(Library, EntryPoint = "xcb_get_keyboard_mapping_keysyms_length")]
    private static partial int MappingKeysymsLength(nint reply);

    /// <summary>Requests the server's modifier map.</summary>
    /// <param name="connection">The XCB connection.</param>
    /// <returns>The request cookie.</returns>
    [LibraryImport(Library, EntryPoint = "xcb_get_modifier_mapping")]
    private static partial Cookie GetModifierMapping(nint connection);

    /// <summary>Receives the modifier mapping.</summary>
    /// <param name="connection">The XCB connection.</param>
    /// <param name="cookie">The request cookie.</param>
    /// <param name="error">Optional native error output.</param>
    /// <returns>The allocated reply, or null on error.</returns>
    [LibraryImport(Library, EntryPoint = "xcb_get_modifier_mapping_reply")]
    private static partial nint ModifierMappingReply(nint connection, Cookie cookie, nint error);

    /// <summary>Gets the modifier keycodes.</summary>
    /// <param name="reply">The native reply.</param>
    /// <returns>The start of the array.</returns>
    [LibraryImport(Library, EntryPoint = "xcb_get_modifier_mapping_keycodes")]
    private static partial nint ModifierKeycodes(nint reply);

    /// <summary>Measures the modifier keycode array.</summary>
    /// <param name="reply">The native reply.</param>
    /// <returns>The number of keycodes.</returns>
    [LibraryImport(Library, EntryPoint = "xcb_get_modifier_mapping_keycodes_length")]
    private static partial int ModifierKeycodesLength(nint reply);

    /// <summary>Asks the X server to reserve a key combination.</summary>
    /// <param name="connection">The XCB connection.</param>
    /// <param name="ownerEvents">Whether to let normally selected events through.</param>
    /// <param name="window">The root window.</param>
    /// <param name="modifiers">The required modifier mask.</param>
    /// <param name="key">The keycode to reserve.</param>
    /// <param name="pointerMode">The pointer processing mode.</param>
    /// <param name="keyboardMode">The keyboard processing mode.</param>
    /// <returns>The checked request cookie.</returns>
    [LibraryImport(Library, EntryPoint = "xcb_grab_key_checked")]
    private static partial Cookie GrabKey(nint connection, byte ownerEvents, uint window, ushort modifiers, byte key, byte pointerMode, byte keyboardMode);

    /// <summary>Synchronizes the registration and checks the reply.</summary>
    /// <param name="connection">The XCB connection.</param>
    /// <param name="cookie">The checked request.</param>
    /// <returns>An allocated X error, or null for success.</returns>
    [LibraryImport(Library, EntryPoint = "xcb_request_check")]
    private static partial nint RequestCheck(nint connection, Cookie cookie);

    /// <summary>Reads one queued event without blocking.</summary>
    /// <param name="connection">The XCB connection.</param>
    /// <returns>The allocated event, or null when the queue is empty.</returns>
    [LibraryImport(Library, EntryPoint = "xcb_poll_for_event")]
    private static partial nint PollForEvent(nint connection);

    /// <summary>The native sequence identifying a request.</summary>
    /// <param name="Sequence">The server request sequence.</param>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Cookie(uint Sequence);

    /// <summary>An iterator over the native screen structures.</summary>
    /// <param name="Data">The current screen pointer.</param>
    /// <param name="Remaining">The number of screens left.</param>
    /// <param name="Index">The byte index maintained by XCB.</param>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct ScreenIterator(nint Data, int Remaining, int Index);
}
