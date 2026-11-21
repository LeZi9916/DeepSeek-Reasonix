using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Net;

namespace Reasonix.Studio.Shell.Browser;

/// <summary>A user-operated system web view, isolated from the session's authenticated HTTP client.</summary>
public sealed partial class ManualWebPreview : UserControl
{
    /// <summary>The private authority this guest must never navigate to.</summary>
    private readonly Uri? _kernel;

    /// <summary>The language of the host controls.</summary>
    private readonly InterfaceLanguage _language;

    /// <summary>Native adapter created only when the reader actually asks for a page.</summary>
    private NativeWebView? _guest;

    /// <summary>Whether this view is showing an explicitly opened workspace HTML snapshot.</summary>
    private bool _localDocument;

    /// <summary>Initializes a new instance of the <see cref="ManualWebPreview"/> class.</summary>
    public ManualWebPreview()
        : this(null, LanguageResolver.FromMachine())
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ManualWebPreview"/> class.</summary>
    /// <param name="kernel">Private session origin.</param>
    /// <param name="language">Window language.</param>
    internal ManualWebPreview(Uri? kernel, InterfaceLanguage language)
    {
        InitializeComponent();
        _kernel = kernel;
        _language = language;
        Go.Content = MessageCatalog.Get(MessageKey.Workbench.Go, language);
        Address.PlaceholderText = MessageCatalog.Get(MessageKey.Workbench.WebAddress, language);
        Label(Address, MessageKey.Workbench.WebAddress);
        Label(Back, MessageKey.Workbench.Back);
        Label(Forward, MessageKey.Workbench.Forward);
        Label(Reload, MessageKey.Common.Reload);
        Go.Click += (_, _) => OpenAddress();
        Address.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                OpenAddress();
            }
        };
        Back.Click += (_, _) => _guest?.GoBack();
        Forward.Click += (_, _) => _guest?.GoForward();
        Reload.Click += (_, _) => _guest?.Refresh();
    }

    /// <summary>Focuses the address field for an explicit navigation.</summary>
    internal void FocusAddress()
    {
        Address.Focus();
        Address.SelectAll();
    }

    /// <summary>Previews local HTML inside a script-free sandbox with no host file URL.</summary>
    /// <param name="html">Contents of the already-granted workspace file.</param>
    internal void ShowDocument(string html)
    {
        _localDocument = true;
        Notice.IsVisible = false;
        try
        {
            var sandbox = "<!doctype html><meta charset=utf-8><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; frame-src 'self' about:; img-src data:; style-src 'unsafe-inline'\"><style>body{margin:0}iframe{width:100vw;height:100vh;border:none}</style><iframe sandbox srcdoc=\""
                + WebUtility.HtmlEncode(html) + "\"></iframe>";
            Guest().NavigateToString(sandbox);
        }
        catch (Exception error)
        {
            ShowFailure(error.Message);
        }
    }

    /// <summary>Validates a typed URL and opens it in the current guest.</summary>
    private void OpenAddress()
    {
        if (!PreviewAddress.TryRead(Address.Text, _kernel, out var address))
        {
            ShowFailure(MessageCatalog.Get(MessageKey.Workbench.EnterAValidHttpOr, _language));
            return;
        }

        _localDocument = false;
        Notice.IsVisible = false;
        try
        {
            Guest().Source = address;
        }
        catch (Exception error)
        {
            ShowFailure(error.Message);
        }
    }

    /// <summary>Creates a platform WebView without a native command or cookie bridge.</summary>
    /// <returns>The guest browser.</returns>
    private NativeWebView Guest()
    {
        if (_guest is not null)
        {
            return _guest;
        }

        var view = new NativeWebView
        {
            AdapterPreference = OperatingSystem.IsWindows()
                ? new[] { WebViewAdapterType.WebView2 }
                : OperatingSystem.IsMacOS() ? new[] { WebViewAdapterType.WkWebView } : new[] { WebViewAdapterType.WpeWebKit, WebViewAdapterType.WebKitGtk },
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
        view.NavigationStarted += (sender, e) =>
        {
            var destination = e.Request?.AbsoluteUri;
            if (_localDocument && destination is null or "about:blank")
            {
                return;
            }

            if (!PreviewAddress.TryRead(destination, _kernel, out _))
            {
                e.Cancel = true;
                ShowFailure(MessageCatalog.Get(MessageKey.Workbench.EnterAValidHttpOr, _language));
            }
        };
        view.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess)
            {
                ShowFailure(MessageCatalog.Get(MessageKey.Workbench.ThisPageCouldNotBe, _language));
            }
            else
            {
                Notice.IsVisible = false;
                if (PreviewAddress.TryRead(e.Request?.AbsoluteUri, _kernel, out var address))
                {
                    Address.Text = address!.AbsoluteUri;
                }
            }

            Back.IsEnabled = view.CanGoBack;
            Forward.IsEnabled = view.CanGoForward;
        };
        view.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (PreviewAddress.TryRead(e.Request?.AbsoluteUri, _kernel, out var target))
            {
                Address.Text = target!.AbsoluteUri;
                FocusAddress();
            }
        };
        view.ProcessTerminated += (_, _) => ShowFailure(MessageCatalog.Get(MessageKey.Workbench.ThisPageCouldNotBe, _language));
        GuestHost.Child = view;
        _guest = view;
        return view;
    }

    /// <summary>Reports an invalid address or native navigation failure.</summary>
    /// <param name="reason">The visible reason.</param>
    private void ShowFailure(string reason)
    {
        Notice.Text = reason;
        Notice.IsVisible = true;
    }

    /// <summary>Labels one native toolbar control.</summary>
    /// <param name="control">The control.</param>
    /// <param name="key">The translation key.</param>
    private void Label(Control control, string key)
    {
        var text = MessageCatalog.Get(key, _language);
        AutomationProperties.SetName(control, text);
        ToolTip.SetTip(control, text);
    }
}
