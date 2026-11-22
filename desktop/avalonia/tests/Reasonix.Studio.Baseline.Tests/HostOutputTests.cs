using Reasonix.Studio.Kernel.Client;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>Holds redirected host output to a nonblocking process lifetime.</summary>
public sealed class HostOutputTests
{
    /// <summary>A chatty child must make progress before and after its handshake.</summary>
    /// <param name="beforeHandshake">Whether stderr fills before the announcement.</param>
    /// <returns>The isolated child-process probe.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RedirectedOutputCannotBlockTheHost(bool beforeHandshake)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Directory.CreateTempSubdirectory("studio-output-");
        LaunchedKernel? kernel = null;
        try
        {
            var marker = Path.Combine(directory.FullName, "ready");
            var pid = Path.Combine(directory.FullName, "pid");
            string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
            var noise = "[Console]::Error.Write(('diagnostic-line-' * 20000));";
            var announcement = "[Console]::Out.WriteLine('{\"version\":1,\"origin\":\"http://127.0.0.1:7411\",\"token\":\"0123456789abcdef0123456789abcdef\"}');";
            var script = "[IO.File]::WriteAllText(" + Quote(pid) + ", $PID.ToString());" + (beforeHandshake ? noise : string.Empty) + announcement + noise + "[Console]::Out.Write(('stdout-line-' * 20000));[IO.File]::WriteAllText(" + Quote(marker) + ", 'ready');Start-Sleep -Seconds 30;";
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            kernel = await HostLauncher.StartAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], stop.Token);
            var until = DateTime.UtcNow.AddSeconds(3);
            while (!File.Exists(marker) && DateTime.UtcNow < until)
            {
                await Task.Delay(10);
            }

            Assert.True(File.Exists(marker), "the redirected host blocked while writing diagnostics");
        }
        finally
        {
            kernel?.Dispose();
            var pidFile = Path.Combine(directory.FullName, "pid");
            if (File.Exists(pidFile) && int.TryParse(await File.ReadAllTextAsync(pidFile), out var id))
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
}
