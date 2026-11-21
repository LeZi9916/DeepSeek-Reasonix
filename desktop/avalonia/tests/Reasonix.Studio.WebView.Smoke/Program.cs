using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Reasonix.Studio.Shell;
using Reasonix.Studio.Shell.Browser;
using System;
using System.Collections.Generic;
using System.IO;
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
        var server = ServeAsync(listener, done.Token);
        var baseUrl = "http://" + listener.LocalEndpoint;
        var window = new MainWindow(false);
        window.ConfigureNativeTitleBar();
        window.Show();
        var exit = 1;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await CheckAsync(window, baseUrl).ConfigureAwait(true);
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
        window.FindControl<MenuItem>("BrowserAction")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var pane = (ManualWebPreview)window.FindControl<ContentControl>("ManualBrowserHost")!.Content!;
        var address = pane.FindControl<TextBox>("Address")!;
        address.Text = baseUrl + "/one";
        Click("Go");
        var guest = (NativeWebView)pane.FindControl<Border>("GuestHost")!.Child!;
        await UntilAsync("document.title", "one").ConfigureAwait(true);
        if (guest.AdapterInfo?.Type is not (WebViewAdapterType.WkWebView or WebViewAdapterType.WebView2 or WebViewAdapterType.WebKitGtk or WebViewAdapterType.WpeWebKit))
        {
            throw new InvalidOperationException("The control did not create a system WebView.");
        }

        address.Text = baseUrl + "/two";
        Click("Go");
        await UntilAsync("document.title", "two").ConfigureAwait(true);
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
        address.Text = "file:///manual-preview-smoke";
        Click("Go");
        if (!pane.FindControl<SelectableTextBlock>("Notice")!.IsVisible)
        {
            throw new InvalidOperationException("The address field accepted a file URL.");
        }

        await UntilAsync("document.title", "two").ConfigureAwait(true);
        pane.ShowDocument("<h1>Workspace preview</h1>");
        await UntilAsync("document.querySelector('iframe')?.getAttribute('srcdoc')?.includes('Workspace preview') && document.querySelector('iframe')?.getAttribute('sandbox') === ''", "true").ConfigureAwait(true);
        Console.WriteLine($"{guest.AdapterInfo.Type}: address, back, forward, reload and workspace HTML passed.");

        void Click(string name) => pane.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        async Task UntilAsync(string expression, string expected)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!timeout.IsCancellationRequested)
            {
                try
                {
                    var result = await guest.InvokeScript(expression).WaitAsync(timeout.Token).ConfigureAwait(true);
                    if (result?.Trim('"') == expected)
                    {
                        return;
                    }
                }
                catch (InvalidOperationException) when (guest.AdapterInfo is null)
                {
                }

                await Task.Delay(50, timeout.Token).ConfigureAwait(true);
            }

            throw new TimeoutException("The guest did not display the expected local page.");
        }
    }

    /// <summary>Serves the two deterministic pages without an external site or a model credential.</summary>
    /// <param name="listener">Loopback listener.</param>
    /// <param name="token">Ends the listener with the test.</param>
    /// <returns>The fixture lifetime.</returns>
    private static async Task ServeAsync(TcpListener listener, CancellationToken token)
    {
        var clients = new List<Task>();
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                clients.Add(WritePageAsync(client, token));
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
    /// <param name="token">Stops the fixture.</param>
    /// <returns>The response write.</returns>
    private static async Task WritePageAsync(TcpClient client, CancellationToken token)
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

                var title = request.StartsWith("GET /two ", StringComparison.Ordinal) ? "two" : "one";
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
