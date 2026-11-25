using Reasonix.Studio.Kernel.Client;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>Holds the pipe a shell lends its kernel for as long as it wants it alive.</summary>
public sealed class HostLeaseTests
{
    /// <summary>A shell that goes away without closing anything still ends the kernel's lease.</summary>
    /// <returns>The isolated child-process probe.</returns>
    [Fact]
    public async Task ClosingTheShellLeaseEndsTheKernel()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Directory.CreateTempSubdirectory("studio-lease-");
        LaunchedKernel? kernel = null;
        try
        {
            var marker = Path.Combine(directory.FullName, "lease-ended");
            var pid = Path.Combine(directory.FullName, "pid");
            string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
            var announcement = "[Console]::Out.WriteLine('{\"version\":1,\"origin\":\"http://127.0.0.1:7412\",\"token\":\"0123456789abcdef0123456789abcdef\"}');";
            var script = "[IO.File]::WriteAllText(" + Quote(pid) + ", $PID.ToString());" + announcement
                + "[Console]::In.ReadLine();[IO.File]::WriteAllText(" + Quote(marker) + ", 'ended');Start-Sleep -Seconds 30;";
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            kernel = await HostLauncher.StartAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], stop.Token);

            Assert.False(kernel.HasExited);
            Assert.NotNull(kernel.Lease);
            Assert.False(await WaitForAsync(() => File.Exists(marker), TimeSpan.FromMilliseconds(250)));

            kernel.Lease.Dispose();

            Assert.True(await WaitForAsync(() => File.Exists(marker), TimeSpan.FromSeconds(3)), "the kernel never observed the closed lease");
        }
        finally
        {
            kernel?.Dispose();
            var pid = Path.Combine(directory.FullName, "pid");
            if (File.Exists(pid) && int.TryParse(await File.ReadAllTextAsync(pid), out var id))
            {
                try
                {
                    using var child = Process.GetProcessById(id);
                    if (!child.HasExited)
                    {
                        child.Kill(true);
                    }
                }
                catch (ArgumentException) { }
            }

            directory.Delete(true);
        }
    }

    /// <summary>Waits briefly for a child process to write its marker.</summary>
    /// <param name="condition">The marker to observe.</param><param name="limit">How long to wait.</param>
    /// <returns>Whether the marker appeared.</returns>
    private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < until)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }
}
