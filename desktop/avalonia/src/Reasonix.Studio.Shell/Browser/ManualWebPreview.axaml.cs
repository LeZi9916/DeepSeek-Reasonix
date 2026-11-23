using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Net;
using System.Text;

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

    /// <summary>The exact generated source authorized for the explicit workspace preview.</summary>
    private Uri? _documentAddress;

    /// <summary>The last time this page was active or navigated.</summary>
    private DateTimeOffset _lastSeen = DateTimeOffset.UtcNow;

    /// <summary>The acknowledged URL retained while the native guest sleeps.</summary>
    private Uri? _sleepAddress;

    /// <summary>Whether the guest is still navigating.</summary>
    private bool _navigating;

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

    /// <summary>Restores a slept browser surface at its previously loaded address.</summary>
    internal void Wake()
    {
        _lastSeen = DateTimeOffset.UtcNow;
        if (_sleepAddress is not { } address)
        {
            return;
        }

        _sleepAddress = null;
        try
        {
            Guest().Source = address;
        }
        catch (Exception error)
        {
            ShowFailure(error.Message);
        }
    }

    /// <summary>Releases a hidden guest after it has remained idle for fifteen minutes.</summary>
    /// <param name="now">Time of the current sweep.</param>
    internal void SleepWhenIdle(DateTimeOffset now)
    {
        if (IsEffectivelyVisible && this.IsAttachedToVisualTree())
        {
            _lastSeen = now;
            return;
        }

        if (_guest is null || _documentAddress is not null || _navigating || now - _lastSeen < TimeSpan.FromMinutes(15)
            || !PreviewAddress.TryRead(_guest.Source?.AbsoluteUri, _kernel, out var address))
        {
            return;
        }

        ClosePage();
        _sleepAddress = address;
    }

    /// <summary>Releases the native guest when its browser tab is explicitly closed.</summary>
    internal void ClosePage()
    {
        GuestHost.Child = null;
        _guest = null;
        _sleepAddress = null;
        _navigating = false;
    }

    /// <summary>Previews local HTML inside a script-free sandbox with no host file URL.</summary>
    /// <param name="html">Contents of the already-granted workspace file.</param>
    internal void ShowDocument(string html)
    {
        Notice.IsVisible = false;
        try
        {
            var sandbox = "<!doctype html><meta charset=utf-8><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; frame-src 'self' about:; img-src data:; style-src 'unsafe-inline'\"><style>body{margin:0}iframe{width:100vw;height:100vh;border:none}</style><iframe sandbox srcdoc=\""
                + WebUtility.HtmlEncode(html) + "\"></iframe>";
            _documentAddress = new Uri("data:text/html;charset=utf-8;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(sandbox)));
            Guest().Source = _documentAddress;
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

        _documentAddress = null;
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

        var view = WorkbenchWebGuest.Create();
        view.NavigationStarted += (sender, e) =>
        {
            _lastSeen = DateTimeOffset.UtcNow;
            _navigating = true;
            var destination = e.Request?.AbsoluteUri;
            if (_documentAddress is not null && (destination is null or "about:blank" || e.Request == _documentAddress))
            {
                return;
            }

            if (!PreviewAddress.TryRead(destination, _kernel, out _))
            {
                e.Cancel = true;
                _navigating = false;
                ShowFailure(MessageCatalog.Get(MessageKey.Workbench.EnterAValidHttpOr, _language));
            }
        };
        view.NavigationCompleted += (_, e) =>
        {
            _navigating = false;
            _lastSeen = DateTimeOffset.UtcNow;
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
