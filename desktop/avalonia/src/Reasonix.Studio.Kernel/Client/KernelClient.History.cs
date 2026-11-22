using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class KernelClient
{
    /// <summary>Reads bounded history, or reports an older host's absent capability.</summary>
    /// <param name="cursor">Opaque host cursor.</param><param name="token">Cancels the read.</param>
    /// <returns>The acknowledged page or null when unsupported.</returns>
    public async Task<HistoryPage?> HistoryPageAsync(string? cursor = null, CancellationToken token = default)
    {
        using var reply = await _http.GetAsync("/history/page" + (string.IsNullOrEmpty(cursor) ? string.Empty : "?cursor=" + Uri.EscapeDataString(cursor)), token).ConfigureAwait(false);
        try
        {
            await EnsureAsync(reply, token).ConfigureAwait(false);
        }
        catch (KernelRefusal error) when (error.Code == "history.pagination_unsupported" || (string.IsNullOrEmpty(error.Code) && error.Status is 404 or 405 or 501))
        {
            return null;
        }

        await using var body = await reply.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<HistoryPage>(body, cancellationToken: token).ConfigureAwait(false);
    }
}
