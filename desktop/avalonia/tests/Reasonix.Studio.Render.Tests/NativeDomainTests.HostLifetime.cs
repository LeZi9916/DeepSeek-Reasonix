using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Waits for the owned Windows process to release SQLite files before removing its profile.</summary>
    /// <param name="hosted">The isolated child process.</param><param name="scratch">Its temporary workspace and profile.</param>
    /// <returns>The acknowledged process exit and profile removal.</returns>
    private static async Task RemoveHostProfileAsync(Process? hosted, DirectoryInfo scratch)
    {
        if (hosted is not null)
        {
            using var stopped = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await hosted.WaitForExitAsync(stopped.Token).ConfigureAwait(false);
            hosted.Dispose();
        }

        scratch.Delete(true);
    }
}
