using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell;
using Reasonix.Studio.Shell.Browser;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.WebView.Smoke;

/// <summary>Drives the workbench preview on the actual desktop adapter against two local pages.</summary>
internal static class Program
{
    /// <summary>Starts a native window and returns the real platform check's exit code.</summary>
    /// <returns>Zero when browsing and workspace document preview both succeed.</returns>
    [STAThread]
    public static int Main()
    {
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().SetupWithoutStarting();
        using var done = new CancellationTokenSource();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var privateListener = new TcpListener(IPAddress.Loopback, 0);
        privateListener.Start();
        var privateOrigin = new Uri("http://" + privateListener.LocalEndpoint);
        var server = ServeAsync(listener, privateOrigin, done.Token);
        var baseUrl = "http://" + listener.LocalEndpoint;
        var window = new MainWindow(false, null, InterfaceLanguage.Zh, _ => Task.FromResult(true));
        window.ConfigureNativeTitleBar();
        window.Show();
        var exit = 1;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await CheckAsync(window, baseUrl).ConfigureAwait(true);
                await CheckRedirectAsync(baseUrl, privateOrigin).ConfigureAwait(true);
                await OfficePreviewSmoke.RunAsync().ConfigureAwait(true);
                window.FindControl<StackPanel>("FileTabs")!.GetVisualDescendants().OfType<Button>()
                    .Single(button => AutomationProperties.GetAutomationId(button) == "action:workbench.close-browser")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                exit = 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
            }
            finally
            {
                window.Close();
                await done.CancelAsync().ConfigureAwait(true);
            }
        });
        Dispatcher.UIThread.MainLoop(done.Token);
        server.GetAwaiter().GetResult();
        return exit;
    }

    /// <summary>Follows links through the same address, history and reload controls used in Studio.</summary>
    /// <param name="window">The native shell.</param>
    /// <param name="baseUrl">The fixture server's origin.</param>
    /// <returns>The platform observation.</returns>
    private static async Task CheckAsync(MainWindow window, string baseUrl)
    {
        window.RequestedThemeVariant = ThemeVariant.Dark;
        window.FindControl<MenuItem>("BrowserAction")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var pane = (ManualWebPreview)window.FindControl<Reasonix.Studio.Controls.StudioSurfaceHost>("ManualBrowserHost")!.Content!;
        var address = pane.FindControl<TextBox>("Address")!;
        address.Text = baseUrl + "/one";
        Click("Go");
        var guest = (NativeWebView)pane.FindControl<Border>("GuestHost")!.Child!;
        var documentReady = false;
        guest.NavigationStarted += (_, _) => documentReady = false;
        guest.NavigationCompleted += (_, e) => documentReady = e.IsSuccess;
        await ObserveLoadingAsync(window, pane, "dark").ConfigureAwait(true);
        await UntilAsync("document.title", "one").ConfigureAwait(true);
        AssertReadyFooter(pane);
        CaptureFooter(window, pane, "dark", "settled");
        if (guest.AdapterInfo?.Type is not (WebViewAdapterType.WkWebView or WebViewAdapterType.WebView2 or WebViewAdapterType.WebKitGtk or WebViewAdapterType.WpeWebKit))
        {
            throw new InvalidOperationException("The control did not create a system WebView.");
        }

        window.RequestedThemeVariant = ThemeVariant.Light;
        address.Text = baseUrl + "/two";
        Click("Go");
        await ObserveLoadingAsync(window, pane, "light").ConfigureAwait(true);
        await UntilAsync("document.title", "two").ConfigureAwait(true);
        AssertReadyFooter(pane);
        CaptureFooter(window, pane, "light", "settled");
        Click("Back");
        await UntilAsync("document.title", "one").ConfigureAwait(true);
        Click("Forward");
        await UntilAsync("document.title", "two").ConfigureAwait(true);
        var reloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        guest.NavigationCompleted += (_, e) =>
        {
            if (e.IsSuccess)
            {
                reloaded.TrySetResult();
            }
        };
        Click("Reload");
        await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        window.FindControl<Button>("NewSurface")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.FindControl<Reasonix.Studio.Shell.Workbench.WorkbenchLauncher>("WorkbenchStart")!.FindControl<Button>("Browser")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var second = (ManualWebPreview)window.FindControl<Reasonix.Studio.Controls.StudioSurfaceHost>("ManualBrowserHost")!.Content!;
        second.FindControl<TextBox>("Address")!.Text = baseUrl + "/one";
        second.FindControl<Button>("Go")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var otherGuest = (NativeWebView)second.FindControl<Border>("GuestHost")!.Child!;
        var otherLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        otherGuest.NavigationCompleted += (_, e) =>
        {
            if (e.IsSuccess)
            {
                otherLoaded.TrySetResult();
            }
        };
        await otherLoaded.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
        second.FindControl<TextBox>("Address")!.Text = baseUrl + "/held";
        second.FindControl<Button>("Go")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        AssertLoadingFooter(second);
        var tabs = window.FindControl<StackPanel>("FileTabs")!;
        tabs.GetVisualDescendants().OfType<Button>().First(button => AutomationProperties.GetAutomationId(button) == "action:workbench.browser-tab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync("document.title", "two").ConfigureAwait(true);
        if (!guest.CanGoBack)
        {
            throw new InvalidOperationException("Switching browser tabs discarded native navigation history.");
        }

        var addSurface = window.FindControl<Button>("NewSurface")!;
        var launcher = window.FindControl<Control>("WorkbenchStart")!;
        addSurface.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!launcher.IsEffectivelyVisible || pane.IsEffectivelyVisible || !guest.IsAttachedToVisualTree())
        {
            throw new InvalidOperationException("The launcher did not cover and retain the selected native web view.");
        }

        addSurface.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync("document.title", "two").ConfigureAwait(true);
        if (launcher.IsEffectivelyVisible || !pane.IsEffectivelyVisible || !guest.CanGoBack)
        {
            throw new InvalidOperationException("Dismissing the launcher did not restore the native web view and history.");
        }

        tabs.GetVisualDescendants().OfType<Button>().Last(button => AutomationProperties.GetAutomationId(button) == "action:workbench.close-browser").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        using var tabExit = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (tabs.GetVisualDescendants().OfType<Button>().Count(button => AutomationProperties.GetAutomationId(button) == "action:workbench.browser-tab") != 1)
        {
            await Task.Delay(12, tabExit.Token).ConfigureAwait(true);
        }

        if (second.FindControl<Border>("BrowserFooter")!.IsVisible || second.FindControl<Border>("GuestHost")!.Child is not null)
        {
            throw new InvalidOperationException("A closed browser still displayed its pending request.");
        }

        Click("Back");
        await UntilAsync("document.title", "one").ConfigureAwait(true);
        Click("Forward");
        await UntilAsync("document.title", "two").ConfigureAwait(true);
        address.Text = "file:///manual-preview-smoke";
        Click("Go");
        if (!pane.FindControl<SelectableTextBlock>("Notice")!.IsVisible)
        {
            throw new InvalidOperationException("The address field accepted a file URL.");
        }

        await UntilAsync("document.title", "two").ConfigureAwait(true);
        pane.ShowDocument("<h1>Workspace preview</h1>");
        await UntilAsync("document.querySelector('iframe')?.getAttribute('srcdoc')?.includes('Workspace preview') && document.querySelector('iframe')?.getAttribute('sandbox') === ''", "true").ConfigureAwait(true);
        if (pane.FindControl<Border>("BrowserFooter")!.IsVisible)
        {
            throw new InvalidOperationException("A sandboxed workspace document exposed an external browser action.");
        }

        window.Classes.Remove("motion");
        address.Text = baseUrl + "/held";
        Click("Go");
        AssertLoadingFooter(pane);
        await Task.Delay(160).ConfigureAwait(true);
        var loadingDot = pane.FindControl<Avalonia.Controls.Shapes.Ellipse>("LoadingDot")!;
        if (loadingDot.Opacity != 1)
        {
            throw new InvalidOperationException("The reduced-motion browser indicator did not remain still.");
        }

        window.Classes.Add("motion");
        address.Text = baseUrl + "/redirect-ok";
        Click("Go");
        AssertLoadingFooter(pane);
        await UntilAsync("document.title", "two").ConfigureAwait(true);
        AssertReadyFooter(pane);
        if (!address.Text!.EndsWith("/two", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The browser did not settle on the redirected address.");
        }

        Console.WriteLine($"{guest.AdapterInfo.Type}: address, tabs, retained history, close, reload, loading feedback, redirect replacement and workspace HTML passed; Windows render scaling {window.RenderScaling}.");

        void Click(string name) => pane.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        async Task UntilAsync(string expression, string expected)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!timeout.IsCancellationRequested)
            {
                if (documentReady)
                {
                    try
                    {
                        var result = await guest.InvokeScript(expression).WaitAsync(timeout.Token).ConfigureAwait(true);
                        if (result?.Trim('"') == expected)
                        {
                            return;
                        }
                    }
                    catch (InvalidOperationException) { }
                }

                await Task.Delay(50, timeout.Token).ConfigureAwait(true);
            }

            throw new TimeoutException("The guest did not display the expected local page.");
        }
    }

    /// <summary>Observes a real navigation refusal after an approved local request redirects.</summary>
    /// <param name="baseUrl">The origin of the fixture that emits the redirect.</param>
    /// <param name="privateOrigin">The separate local authority reserved for the navigation guard.</param>
    /// <returns>The browser boundary smoke check.</returns>
    private static async Task CheckRedirectAsync(string baseUrl, Uri privateOrigin)
    {
        var pane = new ManualWebPreview(privateOrigin, InterfaceLanguage.Zh);
        var window = new Window { Width = 520, Height = 400, Content = pane };
        window.Show();
        try
        {
            pane.FindControl<TextBox>("Address")!.Text = baseUrl + "/blocked";
            pane.FindControl<Button>("Go")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            AssertLoadingFooter(pane);
            using var blocked = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!pane.FindControl<SelectableTextBlock>("Notice")!.IsVisible)
            {
                await Task.Delay(20, blocked.Token).ConfigureAwait(true);
            }

            AssertReadyFooter(pane);
            Console.WriteLine("A blocked native redirect retired the loading pulse.");
        }
        finally
        {
            pane.ClosePage();
            window.Close();
        }
    }

    /// <summary>Checks that an approved pending navigation shows the loading pulse and the launcher action.</summary>
    /// <param name="pane">The browser under test.</param>
    private static void AssertLoadingFooter(ManualWebPreview pane)
    {
        if (!pane.FindControl<Border>("BrowserFooter")!.IsVisible ||
            !pane.FindControl<StackPanel>("BrowserLoading")!.IsVisible ||
            !pane.FindControl<Button>("BrowserExternal")!.IsEnabled)
        {
            throw new InvalidOperationException("The active browser request did not expose its loading feedback.");
        }
    }

    /// <summary>Checks that navigation settlement leaves the launcher action without the loading pulse.</summary>
    /// <param name="pane">The browser under test.</param>
    private static void AssertReadyFooter(ManualWebPreview pane)
    {
        if (!pane.FindControl<Border>("BrowserFooter")!.IsVisible ||
            pane.FindControl<StackPanel>("BrowserLoading")!.IsVisible ||
            !pane.FindControl<Button>("BrowserExternal")!.IsEnabled)
        {
            throw new InvalidOperationException("The loading footer did not settle with the native navigation.");
        }
    }

    /// <summary>Observes the managed pulse before the delayed local WebView response.</summary>
    /// <param name="window">The current desktop window.</param>
    /// <param name="pane">The browser under test.</param>
    /// <param name="scheme">The palette used in the comparison.</param>
    /// <returns>The observed loading phase.</returns>
    private static async Task ObserveLoadingAsync(MainWindow window, ManualWebPreview pane, string scheme)
    {
        AssertLoadingFooter(pane);
        var dot = pane.FindControl<Avalonia.Controls.Shapes.Ellipse>("LoadingDot")!;
        var wait = System.Diagnostics.Stopwatch.StartNew();
        while (wait.Elapsed < TimeSpan.FromMilliseconds(720))
        {
            if (dot.Opacity is > 0.59 and < 0.672)
            {
                CaptureFooter(window, pane, scheme, "165");
                return;
            }

            await Task.Delay(8).ConfigureAwait(true);
        }

        throw new InvalidOperationException("The real browser loading dot did not reach its moving state.");
    }

    /// <summary>Writes a partial capture of the managed footer next to its Electron sample.</summary>
    /// <param name="window">The current desktop window.</param>
    /// <param name="pane">The browser under test.</param>
    /// <param name="scheme">The palette used in the comparison.</param>
    /// <param name="phase">The captured navigation stage.</param>
    private static void CaptureFooter(MainWindow window, ManualWebPreview pane, string scheme, string phase)
    {
        var output = Environment.GetEnvironmentVariable("STUDIO_MOTION_OUTPUT");
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        window.UpdateLayout();
        var footer = pane.FindControl<Border>("BrowserFooter")!;
        var at = footer.TranslatePoint(default, window)!.Value;
        const int Width = 516;
        const int Height = 47;
        var bounds = new Rect(Math.Floor(at.X) - 4, Math.Floor(at.Y) - 4, Width, Height);
        using var full = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1), new Vector(96, 96));
        full.Render(window);
        using var crop = new RenderTargetBitmap(new PixelSize(Width, Height), new Vector(96, 96));
        using (var context = crop.CreateDrawingContext())
        {
            context.DrawImage(full, bounds, new Rect(new Size(Width, Height)));
        }

        Directory.CreateDirectory(output);
        var stem = Path.Combine(output, $"native-browser-loading-{scheme}-{phase}");
        crop.Save(stem + ".png", PngBitmapEncoderOptions.Default);
        var opacity = pane.FindControl<Avalonia.Controls.Shapes.Ellipse>("LoadingDot")!.Opacity;
        File.WriteAllText(stem + ".txt", FormattableString.Invariant($"{at.X},{at.Y},{footer.Bounds.Width},{footer.Bounds.Height},{opacity}"));
    }

    /// <summary>Serves the two deterministic pages without an external site or a model credential.</summary>
    /// <param name="listener">Loopback listener.</param>
    /// <param name="privateOrigin">The synthetic private authority used by the redirect fixture.</param>
    /// <param name="token">Ends the listener with the test.</param>
    /// <returns>The fixture lifetime.</returns>
    private static async Task ServeAsync(TcpListener listener, Uri privateOrigin, CancellationToken token)
    {
        var clients = new List<Task>();
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                clients.Add(WritePageAsync(client, privateOrigin, token));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            await Task.WhenAll(clients).ConfigureAwait(false);
        }
    }

    /// <summary>Answers one connection while the listener continues accepting WebView preconnects.</summary>
    /// <param name="client">The accepted connection.</param>
    /// <param name="privateOrigin">The synthetic private authority used by the redirect fixture.</param>
    /// <param name="token">Stops the fixture.</param>
    /// <returns>The response write.</returns>
    private static async Task WritePageAsync(TcpClient client, Uri privateOrigin, CancellationToken token)
    {
        using (client)
        {
            try
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var request = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (request is null)
                {
                    return;
                }

                while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { Length: > 0 })
                {
                }

                var blocked = request.StartsWith("GET /blocked ", StringComparison.Ordinal);
                if (blocked || request.StartsWith("GET /redirect-ok ", StringComparison.Ordinal))
                {
                    await Task.Delay(240, token).ConfigureAwait(false);
                    var target = blocked ? privateOrigin.AbsoluteUri : "/two";
                    var redirect = Encoding.ASCII.GetBytes($"HTTP/1.1 302 Found\r\nLocation: {target}\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");
                    await stream.WriteAsync(redirect, token).ConfigureAwait(false);
                    return;
                }

                var title = request.StartsWith("GET /two ", StringComparison.Ordinal) ? "two" : "one";
                var delay = request.StartsWith("GET /held ", StringComparison.Ordinal) ? 5000 : 950;
                await Task.Delay(delay, token).ConfigureAwait(false);
                var html = Encoding.UTF8.GetBytes("<!doctype html><title>" + title + "</title><p>" + title + "</p>");
                var headers = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nConnection: close\r\nContent-Length: " + html.Length + "\r\n\r\n");
                await stream.WriteAsync(headers, token).ConfigureAwait(false);
                await stream.WriteAsync(html, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
                // A browser may abandon a speculative connection.
            }
        }
    }
}
