using Avalonia.Controls;
using System;
using System.Runtime.InteropServices;

namespace Reasonix.Studio.Shell.Host;

/// <summary>Publishes the window's unread state on native desktop surfaces.</summary>
internal static partial class DesktopUnreadBadge
{
    /// <summary>The macOS Objective-C runtime.</summary>
    private const string ObjectiveC = "/usr/lib/libobjc.A.dylib";

    /// <summary>Applies or clears an unread badge without requiring a tray.</summary>
    /// <param name="window">The application window.</param><param name="unread">Whether any conversation is unread.</param>
    /// <param name="description">Accessible text for the badge.</param>
    internal static void Set(Window window, bool unread, string description)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                var app = Send(GetClass("NSApplication"), Selector("sharedApplication"));
                var tile = Send(app, Selector("dockTile"));
                var badge = unread ? SendText(GetClass("NSString"), Selector("stringWithUTF8String:"), "•") : 0;
                SendValue(tile, Selector("setBadgeLabel:"), badge);
            }
            else if (OperatingSystem.IsWindows() && window.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } platform)
            {
                SetWindows(platform.Handle, unread, description);
            }
        }
        catch (Exception error) when (error is ExternalException or DllNotFoundException or EntryPointNotFoundException or InvalidCastException)
        {
            ShellLog.Line("unread badge: " + error.Message);
        }
    }

    /// <summary>Resolves an Objective-C class in the active application runtime.</summary>
    /// <param name="name">Class name.</param><returns>The class pointer.</returns>
    [LibraryImport(ObjectiveC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetClass(string name);

    /// <summary>Gets the identifier for an Objective-C method.</summary>
    /// <param name="name">Method selector.</param><returns>The selector pointer.</returns>
    [LibraryImport(ObjectiveC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Selector(string name);

    /// <summary>Invokes an Objective-C getter.</summary>
    /// <param name="receiver">The object receiving the message.</param><param name="selector">The method.</param><returns>The value.</returns>
    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);

    /// <summary>Creates an application-owned string for the Dock tile.</summary>
    /// <param name="receiver">The string class.</param><param name="selector">The factory method.</param><param name="text">The badge.</param><returns>The string pointer.</returns>
    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint SendText(nint receiver, nint selector, string text);

    /// <summary>Sets an Objective-C property.</summary>
    /// <param name="receiver">The object receiving the message.</param><param name="selector">The setter.</param><param name="value">The new value.</param>
    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static partial void SendValue(nint receiver, nint selector, nint value);
}
