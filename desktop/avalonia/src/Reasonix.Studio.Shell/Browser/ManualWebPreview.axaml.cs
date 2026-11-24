using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;

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

    /// <summary>The validated browser page offered to the system launcher.</summary>
    private Uri? _externalAddress;

    /// <summary>Whether the current navigation began from the address field.</summary>
    private bool _addressNavigation;

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
        LoadingLabel.Text = MessageCatalog.Get(MessageKey.Workbench.OpeningPage, language);
        BrowserExternal.Content = MessageCatalog.Get(MessageKey.Workbench.OpenInExternalBrowser, language);
        BrowserExternal.Click += async (_, _) => await OpenExternalPageAsync().ConfigureAwait(true);
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
        NavigateAddress(address);
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
        _guest = null;
        GuestHost.Child = null;
        _sleepAddress = null;
        _documentAddress = null;
        _externalAddress = null;
        SetNavigating(false);
    }

    /// <summary>Previews local HTML inside a script-free sandbox with no host file URL.</summary>
    /// <param name="html">Contents of the already-granted workspace file.</param>
    internal void ShowDocument(string html)
    {
        Notice.IsVisible = false;
        _externalAddress = null;
        _addressNavigation = false;
        SetNavigating(true);
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
            ShowNotice(MessageCatalog.Get(MessageKey.Workbench.EnterAValidHttpOr, _language));
            return;
        }

        NavigateAddress(address);
    }

    /// <summary>Starts feedback immediately for a validated address, including when the adapter is starting.</summary>
    /// <param name="address">The web address accepted by the preview boundary.</param>
    private void NavigateAddress(Uri address)
    {
        _documentAddress = null;
        Notice.IsVisible = false;
        _externalAddress = address;
        _addressNavigation = true;
        SetNavigating(true);
        try
        {
            var view = Guest();
            if (view.Source == address)
            {
                view.Refresh();
            }
            else
            {
                view.Source = address;
            }
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
        view.NavigationStarted += (_, e) =>
        {
            if (!ReferenceEquals(_guest, view))
            {
                e.Cancel = true;
                return;
            }

            _lastSeen = DateTimeOffset.UtcNow;
            var destination = e.Request?.AbsoluteUri;
            if (_documentAddress is not null && (destination is null or "about:blank" || e.Request == _documentAddress))
            {
                SetNavigating(true);
                return;
            }

            if (!PreviewAddress.TryRead(destination, _kernel, out var address))
            {
                e.Cancel = true;
                ShowFailure(MessageCatalog.Get(MessageKey.Workbench.EnterAValidHttpOr, _language));
                return;
            }

            _documentAddress = null;
            _externalAddress = address;
            Notice.IsVisible = false;
            SetNavigating(true);
        };
        view.NavigationCompleted += (_, e) =>
        {
            if (!ReferenceEquals(_guest, view) || !CompletesActiveNavigation(e.Request))
            {
                return;
            }

            SetNavigating(false);
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
                    Address.Text = address.AbsoluteUri;
                }
            }

            Back.IsEnabled = view.CanGoBack;
            Forward.IsEnabled = view.CanGoForward;
        };
        view.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (ReferenceEquals(_guest, view) && PreviewAddress.TryRead(e.Request?.AbsoluteUri, _kernel, out var target))
            {
                Address.Text = target.AbsoluteUri;
                FocusAddress();
            }
        };
        view.ProcessTerminated += (_, _) =>
        {
            if (ReferenceEquals(_guest, view))
            {
                ShowFailure(MessageCatalog.Get(MessageKey.Workbench.ThisPageCouldNotBe, _language));
            }
        };
        _guest = view;
        GuestHost.Child = view;
        return view;
    }

    /// <summary>Reports an invalid address or native navigation failure.</summary>
    /// <param name="reason">The visible reason.</param>
    private void ShowFailure(string reason)
    {
        SetNavigating(false);
        ShowNotice(reason);
    }

    /// <summary>Shows an address or launcher refusal without changing a different navigation.</summary>
    /// <param name="reason">The visible reason.</param>
    private void ShowNotice(string reason)
    {
        Notice.Text = reason;
        Notice.IsVisible = true;
    }

    /// <summary>Ignores the completion of a page superseded by another address.</summary>
    /// <param name="request">The address reported by the platform, if available.</param>
    /// <returns>Whether this completion can settle the current navigation.</returns>
    private bool CompletesActiveNavigation(Uri? request)
    {
        if (request is null || !_navigating)
        {
            return true;
        }

        if (_documentAddress is { } document)
        {
            return request == document || request.AbsoluteUri == "about:blank";
        }

        return _externalAddress is not { } active ||
            Uri.Compare(request, active, UriComponents.HttpRequestUrl, UriFormat.SafeUnescaped, StringComparison.Ordinal) == 0;
    }

    /// <summary>Updates the existing navigation state and the footer associated with typed addresses.</summary>
    /// <param name="active">Whether the native guest still has a navigation in flight.</param>
    private void SetNavigating(bool active)
    {
        _navigating = active;
        _addressNavigation &= active;
        BrowserFooter.IsVisible = _externalAddress is not null;
        BrowserLoading.IsVisible = _addressNavigation && BrowserFooter.IsVisible;
        LoadingDot.Classes.Set("browser-loading-dot", BrowserLoading.IsVisible);
    }

    /// <summary>Opens the validated page through the platform launcher after the reader clicks the footer.</summary>
    /// <returns>The platform operation.</returns>
    private async Task OpenExternalPageAsync()
    {
        if (_externalAddress is not { } target || TopLevel.GetTopLevel(this)?.Launcher is not { } launcher)
        {
            return;
        }

        var opened = false;
        try
        {
            opened = await launcher.LaunchUriAsync(target).ConfigureAwait(true);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
        }

        if (!opened && ReferenceEquals(_externalAddress, target))
        {
            ShowNotice(MessageCatalog.Get(MessageKey.Common.CouldNotOpenABrowser, _language, ("at", target.AbsoluteUri)));
        }
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
