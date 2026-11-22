using Reasonix.Studio.Contracts.Refusal;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Changes the current task's work strategy.</summary>
    /// <param name="plan">Whether to plan first.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The acknowledgement.</returns>
    public Task SetPlanAsync(bool plan, CancellationToken token) => PostAsync("/plan", new JsonObject { ["on"] = plan }, token);

    /// <summary>Changes the current task's approval boundary.</summary>
    /// <param name="mode">Kernel approval mode.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The acknowledgement.</returns>
    public Task SetApprovalModeAsync(string mode, CancellationToken token) => PostAsync("/tool-approval-mode", new JsonObject { ["mode"] = mode }, token);

    /// <summary>Offers a clearer version of the draft without submitting it.</summary>
    /// <param name="draft">Original draft.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The proposed text.</returns>
    public async Task<string> RefinePromptAsync(string draft, CancellationToken token) =>
        (string)(await DataPostAsync("/prompt/refine", new JsonObject { ["draft"] = draft }, token).ConfigureAwait(false))["text"]!;

    /// <summary>Reads the host's current context accounting.</summary>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The measured breakdown.</returns>
    public Task<JsonObject?> ContextAsync(CancellationToken token) => OptionalPreferenceAsync("/context", token);

    /// <summary>Reads the selected provider's wallet without treating an unsupported wallet as empty.</summary>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The provider's display and freshness, or no wallet.</returns>
    public async Task<JsonObject?> BalanceAsync(CancellationToken token)
    {
        using var response = await _http.GetAsync("/balance", token).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
        {
            return null;
        }

        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: token).ConfigureAwait(false);
    }

    /// <summary>Creates an isolated, read-only side conversation.</summary>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The host-issued identity.</returns>
    public async Task<string> OpenSideAsync(CancellationToken token) =>
        (string)(await DataPostAsync("/side", new JsonObject(), token).ConfigureAwait(false))["id"]!;

    /// <summary>Closes the side conversation on its owning runtime.</summary>
    /// <param name="id">Host-issued identity.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The acknowledgement.</returns>
    public async Task CloseSideAsync(string id, CancellationToken token)
    {
        using var response = await _http.DeleteAsync("/side/" + Uri.EscapeDataString(id), token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
    }

    /// <summary>Reads a side answer through its private stream, requiring the final completion frame.</summary>
    /// <param name="id">Host-issued identity.</param><param name="question">Side question.</param>
    /// <param name="progress">Text progress on the calling context.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The completed answer.</returns>
    public async Task<string> AskSideAsync(string id, string question, IProgress<string> progress, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/side/" + Uri.EscapeDataString(id) + "/ask")
        {
            Content = JsonContent.Create(new { question, quotes = Array.Empty<string>() }),
        };
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false));
        var name = string.Empty;
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
        {
            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                name = line[6..].TrimStart();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data.AppendLine(line[5..].TrimStart());
            }
            else if (line.Length == 0 && data.Length > 0)
            {
                var frame = JsonNode.Parse(data.ToString())!;
                data.Clear();
                if (name == "done" && frame["answer"] is JsonValue answer)
                {
                    return answer.GetValue<string>();
                }

                if (name == "error")
                {
                    throw new KernelRefusal(request.RequestUri?.ToString(), (int?)frame["status"] ?? 502, frame.Deserialize<Reason>(), null);
                }

                if (name == "progress" && (string?)frame["kind"] == "text")
                {
                    progress.Report((string?)frame["text"] ?? string.Empty);
                }

                name = string.Empty;
            }
        }

        throw new KernelRefusal(request.RequestUri?.ToString(), 502, new Reason { Code = "side.failed", Error = "The side answer ended before completion" }, null);
    }
}
