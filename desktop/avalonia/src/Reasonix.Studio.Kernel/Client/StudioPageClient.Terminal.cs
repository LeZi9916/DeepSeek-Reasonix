using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Lists PTYs owned by the current kernel.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The terminal directory.</returns>
    public Task<JsonArray> TerminalsAsync(CancellationToken token) => ReadAsync<JsonArray>("/terminals", token);

    /// <summary>Starts the user's default shell in the kernel's workspace.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The identifier assigned by the kernel.</returns>
    public async Task<string> OpenTerminalAsync(CancellationToken token)
    {
        var request = new JsonObject { ["shell"] = string.Empty, ["cols"] = 80, ["rows"] = 24 };
        using var response = await _http.PostAsJsonAsync("/terminals", request, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        var opened = await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
        return opened["id"]!.GetValue<string>();
    }

    /// <summary>Writes user input to an explicitly opened PTY.</summary>
    /// <param name="id">Terminal id.</param>
    /// <param name="text">The keys encoded by the terminal emulator.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The acknowledgement.</returns>
    public Task TerminalInputAsync(string id, string text, CancellationToken token)
    {
        return PostAsync(TerminalPath(id) + "/input", new JsonObject { ["data"] = text }, token);
    }

    /// <summary>Resizes the PTY's character grid.</summary>
    /// <param name="id">Terminal id.</param>
    /// <param name="cols">Measured columns.</param>
    /// <param name="rows">Measured rows.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The acknowledgement.</returns>
    public Task TerminalSizeAsync(string id, int cols, int rows, CancellationToken token)
    {
        return PostAsync(TerminalPath(id) + "/resize", new JsonObject { ["cols"] = cols, ["rows"] = rows }, token);
    }

    /// <summary>Closes the selected terminal through its owning kernel.</summary>
    /// <param name="id">Terminal id.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The acknowledgement.</returns>
    public async Task CloseTerminalAsync(string id, CancellationToken token)
    {
        using var response = await _http.DeleteAsync(TerminalPath(id), token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
    }

    /// <summary>Reads the initial replay, live output and recorded exit from the PTY stream.</summary>
    /// <param name="id">Terminal id.</param>
    /// <param name="token">Cancels the stream when the view leaves.</param>
    /// <returns>Terminal frames in host order.</returns>
    public async IAsyncEnumerable<TerminalFrame> TerminalOutputAsync(string id, [EnumeratorCancellation] CancellationToken token)
    {
        using var response = await _http.GetAsync(TerminalPath(id) + "/stream", HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
        {
            if (line.StartsWith("data:", StringComparison.Ordinal) && JsonSerializer.Deserialize<TerminalFrame>(line[5..], JsonSerializerOptions.Web) is { } frame)
            {
                yield return frame;
            }
        }
    }

    /// <summary>Names an endpoint below the selected PTY.</summary>
    /// <param name="id">Kernel terminal id.</param>
    /// <returns>The escaped path.</returns>
    private static string TerminalPath(string id) => "/terminals/" + Uri.EscapeDataString(id);
}
