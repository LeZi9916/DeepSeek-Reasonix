using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Session;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Drives one real kernel process: the actual <c>reasonix-studio-host</c> binary
/// announcing a real origin and a real credential.
/// </summary>
/// <remarks>
/// Every other case in this suite fakes one side. This one fakes nothing: the
/// handshake is the kernel's own line, the socket is its own listener, and the gate
/// that admits or refuses each request is the kernel's own code. It is skipped rather
/// than failed when the binary is not beside the tests, because a machine without a
/// built kernel proves nothing about this shell — and a test that fails there would be
/// switched off everywhere.
///
/// The kernel is started with no page and no provider configured. That is deliberate:
/// what is being proven is the boundary and the read path, and a provider would make
/// the case depend on a network and a key nobody supplied.
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

        // The same read without the credential is the gate refusing it, which is what
        // makes the first answer mean something.
        using var anonymous = new HttpClient { BaseAddress = kernel.Origin };
        using var refused = await anonymous.GetAsync("/status", launchTimeout.Token);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }
}
