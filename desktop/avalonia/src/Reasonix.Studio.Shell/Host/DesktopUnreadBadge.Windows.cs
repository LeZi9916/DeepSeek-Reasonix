using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Reasonix.Studio.Shell.Host;

internal static partial class DesktopUnreadBadge
{
    /// <summary>The Windows taskbar interface through the overlay slot.</summary>
    [ComImport]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEA84")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        /// <summary>Initializes the taskbar interface.</summary>
        void Initialize();

        /// <summary>Adds a window to the taskbar.</summary>
        /// <param name="window">The window handle.</param>
        void AddTab(nint window);

        /// <summary>Removes a window from the taskbar.</summary>
        /// <param name="window">The window handle.</param>
        void DeleteTab(nint window);

        /// <summary>Activates a taskbar entry.</summary>
        /// <param name="window">The window handle.</param>
        void ActivateTab(nint window);

        /// <summary>Sets the active alternate taskbar entry.</summary>
        /// <param name="window">The window handle.</param>
        void SetActiveAlt(nint window);

        /// <summary>Marks a window as full screen.</summary>
        /// <param name="window">The window handle.</param><param name="fullscreen">Whether the window is full screen.</param>
        void MarkFullscreenWindow(nint window, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);

        /// <summary>Updates the taskbar progress value.</summary>
        /// <param name="window">The window handle.</param><param name="completed">The completed work.</param><param name="total">The total work.</param>
        void SetProgressValue(nint window, ulong completed, ulong total);

        /// <summary>Updates the taskbar progress state.</summary>
        /// <param name="window">The window handle.</param><param name="flags">The operation flags.</param>
        void SetProgressState(nint window, uint flags);

        /// <summary>Registers a thumbnail tab.</summary>
        /// <param name="tab">The tab handle.</param><param name="window">The window handle.</param>
        void RegisterTab(nint tab, nint window);

        /// <summary>Unregisters a thumbnail tab.</summary>
        /// <param name="tab">The tab handle.</param>
        void UnregisterTab(nint tab);

        /// <summary>Reorders thumbnail tabs.</summary>
        /// <param name="tab">The tab handle.</param><param name="before">The next tab handle.</param>
        void SetTabOrder(nint tab, nint before);

        /// <summary>Activates a thumbnail tab.</summary>
        /// <param name="tab">The tab handle.</param><param name="window">The window handle.</param><param name="flags">The operation flags.</param>
        void SetTabActive(nint tab, nint window, uint flags);

        /// <summary>Adds thumbnail buttons.</summary>
        /// <param name="window">The window handle.</param><param name="count">The button count.</param><param name="buttons">The button array.</param>
        void ThumbBarAddButtons(nint window, uint count, nint buttons);

        /// <summary>Updates thumbnail buttons.</summary>
        /// <param name="window">The window handle.</param><param name="count">The button count.</param><param name="buttons">The button array.</param>
        void ThumbBarUpdateButtons(nint window, uint count, nint buttons);

        /// <summary>Supplies thumbnail button images.</summary>
        /// <param name="window">The window handle.</param><param name="images">The image list.</param>
        void ThumbBarSetImageList(nint window, nint images);

        /// <summary>Applies the status icon to the taskbar window.</summary>
        /// <param name="window">The window handle.</param><param name="icon">The badge icon, or null to remove it.</param><param name="description">The accessible badge description.</param>
        void SetOverlayIcon(nint window, nint icon, [MarshalAs(UnmanagedType.LPWStr)] string? description);
    }

    /// <summary>Sets a Windows taskbar overlay. The shell copies the icon during the call.</summary>
    /// <param name="window">Native window handle.</param><param name="unread">Whether a badge is needed.</param>
    /// <param name="description">The accessibility description.</param>
    [SupportedOSPlatform("windows")]
    private static void SetWindows(nint window, bool unread, string description)
    {
        var taskbarType = Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11D0-958A-006097C9A090"));
        if (taskbarType is null || Activator.CreateInstance(taskbarType) is not ITaskbarList3 taskbar)
        {
            return;
        }

        nint icon = 0;
        try
        {
            taskbar.Initialize();
            if (unread)
            {
                var image = MakeBadgeIcon();
                icon = CreateIconFromResourceEx(image, (uint)image.Length, true, 0x00030000, 16, 16, 0);
                if (icon == 0)
                {
                    return;
                }
            }

            taskbar.SetOverlayIcon(window, icon, unread ? description : null);
        }
        finally
        {
            if (icon != 0)
            {
                DestroyIcon(icon);
            }

            Marshal.ReleaseComObject(taskbar);
        }
    }

    /// <summary>Builds a 32-bit DIB with the taskbar's 16-pixel badge.</summary>
    /// <returns>The icon image and transparency mask.</returns>
    private static byte[] MakeBadgeIcon()
    {
        const int Header = 40;
        const int Size = 16;
        var icon = new byte[Header + (Size * Size * 4) + (Size * 4)];
        BinaryPrimitives.WriteInt32LittleEndian(icon, Header);
        BinaryPrimitives.WriteInt32LittleEndian(icon.AsSpan(4), Size);
        BinaryPrimitives.WriteInt32LittleEndian(icon.AsSpan(8), Size * 2);
        BinaryPrimitives.WriteInt16LittleEndian(icon.AsSpan(12), 1);
        BinaryPrimitives.WriteInt16LittleEndian(icon.AsSpan(14), 32);
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var distance = Math.Sqrt(Math.Pow(x - 7.5, 2) + Math.Pow(y - 7.5, 2));
                var alpha = Math.Clamp(6.25 - distance, 0, 1);
                var inner = Math.Clamp(5 - distance, 0, 1);
                var offset = Header + (((y * Size) + x) * 4);
                icon[offset] = icon[offset + 1] = (byte)Math.Round((255 - (191 * inner)) * alpha);
                icon[offset + 2] = (byte)Math.Round((255 - (31 * inner)) * alpha);
                icon[offset + 3] = (byte)Math.Round(255 * alpha);
                if (alpha == 0)
                {
                    icon[Header + (Size * Size * 4) + (y * 4) + (x / 8)] |= (byte)(0x80 >> (x % 8));
                }
            }
        }

        return icon;
    }

    /// <summary>Creates an icon from a Windows bitmap resource.</summary>
    /// <param name="bits">The bitmap resource.</param><param name="size">Resource length.</param><param name="icon">Whether this is an icon.</param>
    /// <param name="version">Resource format version.</param><param name="width">Requested pixels across.</param><param name="height">Requested pixels down.</param>
    /// <param name="flags">Resource load options.</param><returns>The owned icon.</returns>
    [LibraryImport("user32.dll")]
    private static partial nint CreateIconFromResourceEx(byte[] bits, uint size, [MarshalAs(UnmanagedType.Bool)] bool icon, uint version, int width, int height, uint flags);

    /// <summary>Releases a copied icon after applying it to the taskbar.</summary>
    /// <param name="icon">The owned icon.</param><returns>Whether it was released.</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);
}
