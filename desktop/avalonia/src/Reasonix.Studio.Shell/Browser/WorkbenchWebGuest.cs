using Avalonia.Controls;
using Avalonia.Platform;
using System;

namespace Reasonix.Studio.Shell.Browser;

/// <summary>Creates the isolated system browser used by workbench surfaces.</summary>
internal static class WorkbenchWebGuest
{
    /// <summary>Creates a guest for the desktop's platform without a persistent profile.</summary>
    /// <returns>The unmounted WebView.</returns>
    internal static NativeWebView Create()
    {
        var view = new NativeWebView
        {
            AdapterPreference = OperatingSystem.IsWindows()
                ? [WebViewAdapterType.WebView2]
                : OperatingSystem.IsMacOS() ? [WebViewAdapterType.WkWebView] : [WebViewAdapterType.WpeWebKit, WebViewAdapterType.WebKitGtk],
        };
        view.EnvironmentRequested += (_, options) =>
        {
            options.EnableDevTools = false;
            switch (options)
            {
                case AppleWKWebViewEnvironmentRequestedEventArgs apple:
                    apple.NonPersistentDataStore = true;
                    break;
                case WindowsWebView2EnvironmentRequestedEventArgs windows:
                    windows.IsInPrivateModeEnabled = true;
                    windows.AllowSingleSignOnUsingOSPrimaryAccount = false;
                    break;
                case GtkWebViewEnvironmentRequestedEventArgs gtk:
                    gtk.EphemeralDataManager = true;
                    break;
            }
        };
        return view;
    }
}
