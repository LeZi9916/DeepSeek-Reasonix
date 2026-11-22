using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>Continuously empties a redirected pipe while retaining only a bounded diagnostic tail.</summary>
internal sealed class ProcessOutputPump : IDisposable
{
    /// <summary>Maximum retained characters, independent of host lifetime.</summary>
    private const int TailLimit = 16384;

    /// <summary>Protects the diagnostic tail.</summary>
    private readonly object _sync = new();

    /// <summary>Last bounded diagnostics; stdout is drained without retaining it.</summary>
    private readonly StringBuilder _tail = new();

    /// <summary>Cancels the pending pipe read when the host closes.</summary>
    private readonly CancellationTokenSource _closing = new();

    /// <summary>The observed background drain.</summary>
    private readonly Task _reading;

    /// <summary>Ensures process and engine owners can release the same drain safely.</summary>
    private int _disposed;

    /// <summary>Initializes a new instance of the <see cref="ProcessOutputPump"/> class.</summary>
    /// <param name="reader">Redirected process pipe.</param><param name="retain">Whether to retain diagnostics.</param>
    internal ProcessOutputPump(StreamReader reader, bool retain)
    {
        _reading = Task.Run(async () =>
        {
            var buffer = new char[2048];
            try
            {
                while (!_closing.IsCancellationRequested)
                {
                    var count = await reader.ReadAsync(buffer.AsMemory(), _closing.Token).ConfigureAwait(false);
                    if (count == 0)
                    {
                        return;
                    }

                    if (!retain)
                    {
                        continue;
                    }

                    lock (_sync)
                    {
                        _tail.Append(buffer, 0, count);
                        if (_tail.Length > TailLimit)
                        {
                            _tail.Remove(0, _tail.Length - TailLimit);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (IOException) { }
        });
    }

    /// <summary>Gets the current bounded diagnostic tail.</summary>
    internal string Tail
    {
        get
        {
            lock (_sync)
            {
                return _tail.ToString();
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _closing.Cancel();
        _ = _reading.ContinueWith(_ => _closing.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
