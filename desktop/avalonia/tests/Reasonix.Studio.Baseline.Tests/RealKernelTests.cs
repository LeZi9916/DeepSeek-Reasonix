using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Session;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Drives one real kernel process: the actual <c>reasonix-studio-host</c> binary
/// announcing a real origin and a real credential.
/// </summary>
/// <remarks>
/// CI supplies the host beside the test assembly. Reads, terminal I/O and the
/// credential check run locally without a provider key.
/// </remarks>
public sealed class RealKernelTests
{
    /// <summary>
    /// Checks that a real kernel announces itself, admits a credentialled read, and
    /// refuses the same read without the credential.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task ARealKernelAdmitsItsOwnCredentialAndRefusesTheRest()
    {
        var binary = HostLauncher.Locate();
        if (binary is null)
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("STUDIO_TEST_HOST_REQUIRED"));
            return;
        }

        using var launchTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var kernel = await HostLauncher.StartAsync(binary, Array.Empty<string>(), launchTimeout.Token)
;

        Assert.StartsWith("http://127.0.0.1:", kernel.Origin.ToString(), StringComparison.Ordinal);

        using var authorized = new HttpClient(
            new Reasonix.Studio.Kernel.Boundary.CredentialHandler(kernel.Cookie()))
        {
            BaseAddress = kernel.Origin,
            Timeout = Timeout.InfiniteTimeSpan,
        };

        var client = new KernelClient(authorized);
        var status = await client.StatusAsync(launchTimeout.Token);

        // The kernel answers a status with no provider configured: the point is that
        // it answered at all, through a gate that accepted this launch's credential.
        Assert.NotNull(status);

        var appearance = await client.Pages.AppearanceAsync(launchTimeout.Token);
        Assert.NotNull(appearance["zoomRange"]);
        var tree = await client.Pages.FilesAsync(string.Empty, string.Empty, launchTimeout.Token);
        Assert.NotNull(tree.Files);
        Assert.NotNull(await client.Pages.ModelsAsync(launchTimeout.Token));
        await CheckTerminalAsync(client.Pages, launchTimeout.Token);

        // The same read without the credential is the gate refusing it, which is what
        // makes the first answer mean something.
        using var anonymous = new HttpClient { BaseAddress = kernel.Origin };
        using var refused = await anonymous.GetAsync("/status", launchTimeout.Token);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    /// <summary>Checks real PTY input, resize, streamed output and close over the same authenticated client.</summary>
    /// <param name="api">The session page endpoints.</param>
    /// <param name="token">Cancels the smoke test.</param>
    /// <returns>The observed output.</returns>
    private static async Task CheckTerminalAsync(StudioPageClient api, CancellationToken token)
    {
        var id = await api.OpenTerminalAsync(token);
        try
        {
            await api.TerminalSizeAsync(id, 90, 30, token);
            const string Marker = "reasonix-terminal-check";
            await api.TerminalInputAsync(id, "echo " + Marker + "\r", token);
            var received = new StringBuilder();
            await foreach (var frame in api.TerminalOutputAsync(id, token))
            {
                if (frame.Data is { Length: > 0 } data)
                {
                    received.Append(Encoding.UTF8.GetString(Convert.FromBase64String(data)));
                    if (received.ToString().Contains(Marker, StringComparison.Ordinal))
                    {
                        return;
                    }
                }
            }

            Assert.Fail("The PTY stream ended before reporting the submitted input.");
        }
        finally
        {
            await api.CloseTerminalAsync(id, token);
        }
    }
}
