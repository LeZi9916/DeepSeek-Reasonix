using Reasonix.Studio.Contracts.Events;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>Serves page-specific reads and writes over the session's authenticated connection.</summary>
public sealed partial class StudioPageClient
{
    /// <summary>The connection shared with the session stream.</summary>
    private readonly HttpClient _http;

    /// <summary>Initializes a new instance of the <see cref="StudioPageClient"/> class.</summary>
    /// <param name="http">Authenticated client with a loopback base address.</param>
    public StudioPageClient(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <summary>Confirms the answers to a kernel question.</summary>
    /// <param name="id">The kernel's question batch ID.</param>
    /// <param name="answers">Answers in question order.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The kernel acknowledgement.</returns>
    public Task AnswerAsync(string id, IReadOnlyList<QuestionSelection> answers, CancellationToken token)
    {
        var request = new
        {
            id,
            answers,
        };
        return PostAsync("/answer", request, token);
    }

    /// <summary>Keeps the question open while its form is being edited.</summary>
    /// <param name="id">The kernel's question batch ID.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The kernel acknowledgement.</returns>
    public Task EngageAsync(string id, CancellationToken token)
    {
        var request = new
        {
            id,
            engage = true,
        };
        return PostAsync("/answer", request, token);
    }

    /// <summary>Lists a workspace directory or searches by name.</summary>
    /// <param name="directory">Workspace-relative folder.</param>
    /// <param name="query">Optional filename query.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The directory entries.</returns>
    public Task<WorkspaceListing> FilesAsync(string directory, string query, CancellationToken token)
    {
        return ReadAsync<WorkspaceListing>("/workspace/files?path=" + Uri.EscapeDataString(directory) + "&q=" + Uri.EscapeDataString(query), token);
    }

    /// <summary>Opens a versioned text file.</summary>
    /// <param name="path">Workspace-relative path.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The text and its revision.</returns>
    public Task<WorkspaceFile> FileAsync(string path, CancellationToken token)
    {
        return ReadAsync<WorkspaceFile>("/workspace/file?path=" + Uri.EscapeDataString(path), token);
    }

    /// <summary>Saves against the revision that was opened.</summary>
    /// <param name="file">The edited text and original revision.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The new canonical snapshot.</returns>
    public async Task<WorkspaceFile> SaveFileAsync(WorkspaceFile file, CancellationToken token)
    {
        using var response = await _http.PutAsJsonAsync("/workspace/file", file, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<WorkspaceFile>(response, token).ConfigureAwait(false);
    }

    /// <summary>Requests completions at the composer caret.</summary>
    /// <param name="line">Current draft.</param>
    /// <param name="cursor">UTF-16 caret offset.</param>
    /// <param name="language">Window language tag.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The replacement span and items.</returns>
    public Task<ComposerCompletion> CompleteAsync(string line, int cursor, string language, CancellationToken token)
    {
        var path = "/complete?line=" + Uri.EscapeDataString(line) + "&cursor=" + cursor.ToString(CultureInfo.InvariantCulture) + "&lang=" + Uri.EscapeDataString(language);
        return ReadAsync<ComposerCompletion>(path, token);
    }

    /// <summary>Resolves dropped or picked paths through the workspace boundary.</summary>
    /// <param name="paths">User-selected local paths.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The reference or refusal for each path.</returns>
    public async Task<DroppedReference[]> DropAsync(IReadOnlyList<string> paths, CancellationToken token)
    {
        var request = new
        {
            paths,
        };
        using var response = await _http.PostAsJsonAsync("/drop", request, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<DroppedReference[]>(response, token).ConfigureAwait(false);
    }

    /// <summary>Uploads a user-pasted image and reads its host-issued reference.</summary>
    /// <param name="png">PNG bytes, positioned at the beginning.</param>
    /// <param name="token">Cancels the upload.</param>
    /// <returns>The attachment reference.</returns>
    public async Task<DroppedReference> PasteImageAsync(Stream png, CancellationToken token)
    {
        using var content = new StreamContent(png);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        using var response = await _http.PostAsync("/attachments/upload?name=clipboard.png&mime=image%2Fpng", content, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<DroppedReference>(response, token).ConfigureAwait(false);
    }

    /// <summary>Sets a reasoning effort advertised by the active model.</summary>
    /// <param name="effort">The kernel's effort value.</param>
    /// <param name="token">Cancels the write.</param>
    /// <returns>The kernel acknowledgement.</returns>
    public Task SetEffortAsync(string effort, CancellationToken token)
    {
        var request = new JsonObject { ["effort"] = effort };
        return PostAsync("/effort", request, token);
    }

    /// <summary>Reads the available chat models.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The catalog in host order.</returns>
    public async Task<IReadOnlyList<ModelChoice>> ModelsAsync(CancellationToken token)
    {
        var catalog = await ReadAsync<ModelCatalog>("/models?answers=chat", token).ConfigureAwait(false);
        return catalog.Models ?? Array.Empty<ModelChoice>();
    }

    /// <summary>Selects a model by its stable reference.</summary>
    /// <param name="model">The reference chosen from the catalog.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The kernel acknowledgement.</returns>
    public Task SetModelAsync(string model, CancellationToken token)
    {
        var request = new
        {
            @ref = model,
        };
        return PostAsync("/model", request, token);
    }

    /// <summary>Asks the kernel to discover the endpoint's protocol and available models.</summary>
    /// <param name="url">User-selected model-service URL.</param>
    /// <param name="apiKey">The credential entered for this URL.</param>
    /// <param name="token">Cancels the probe.</param>
    /// <returns>The models and capabilities the service announced.</returns>
    public async Task<ProviderProbe> ProbeAsync(string url, string apiKey, CancellationToken token)
    {
        var request = new
        {
            baseUrl = url,
            apiKey,
        };
        using var response = await _http.PostAsJsonAsync("/providers/probe", request, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<ProviderProbe>(response, token).ConfigureAwait(false);
    }

    /// <summary>Creates a model service and selects the chosen model for this and future sessions.</summary>
    /// <param name="name">User-approved provider name.</param>
    /// <param name="url">Probed URL.</param>
    /// <param name="apiKey">Key entered for this service.</param>
    /// <param name="probe">The kernel's model inventory and transport choices.</param>
    /// <param name="model">A model selected from the inventory.</param>
    /// <param name="token">Cancels the requests.</param>
    /// <returns>The acknowledgements.</returns>
    public async Task ConfigureProviderAsync(string name, string url, string apiKey, ProviderProbe probe, string model, CancellationToken token)
    {
        var request = new
        {
            name,
            kind = probe.Kind,
            baseUrl = probe.BaseUrl ?? url,
            apiKey,
            models = probe.Models,
            @default = model,
            authHeader = probe.AuthHeader,
            noProxy = probe.NoProxy,
            effort = string.Empty,
            vision = probe.Vision ?? Array.Empty<string>(),
        };
        await PostAsync("/providers", request, token).ConfigureAwait(false);
        var reference = name + "/" + model;
        await SetModelAsync(reference, token).ConfigureAwait(false);
        await SetDefaultModelAsync(reference, token).ConfigureAwait(false);
    }

    /// <summary>Sets the default model for future sessions.</summary>
    /// <param name="reference">Provider-qualified model id.</param>
    /// <param name="token">Cancels the write.</param>
    /// <returns>The kernel acknowledgement.</returns>
    public Task SetDefaultModelAsync(string reference, CancellationToken token)
    {
        var request = new
        {
            @ref = reference,
        };
        return PostAsync("/default-model", request, token);
    }

    /// <summary>Returns the kernel-recorded trajectory, including its declared availability.</summary>
    /// <param name="token">Cancels the read.</param>
    /// <returns>The original event record.</returns>
    public Task<JsonObject> TrajectoryAsync(CancellationToken token) => ReadAsync<JsonObject>("/trajectory", token);

    /// <summary>Reads appearance preferences including the host's supported zoom range.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The current appearance.</returns>
    public Task<JsonObject> AppearanceAsync(CancellationToken token)
    {
        return ReadAsync<JsonObject>("/appearance", token);
    }

    /// <summary>Changes the explicit language or zoom while preserving every other writable appearance field.</summary>
    /// <param name="language">Language tag, empty for automatic, or null to keep.</param>
    /// <param name="zoom">Requested zoom, or null to keep.</param>
    /// <param name="token">Cancels the write.</param>
    /// <returns>The normalized appearance.</returns>
    public async Task<JsonObject> SetAppearanceAsync(string? language, double? zoom, CancellationToken token)
    {
        var current = await AppearanceAsync(token).ConfigureAwait(false);
        var paper = current["wallpaper"] as JsonObject;
        var patch = new
        {
            language = language ?? current["language"]?.GetValue<string>() ?? string.Empty,
            zoom = zoom ?? (double?)current["zoom"] ?? 0,
            readSize = (double?)current["readSize"] ?? 0,
            textSize = (double?)current["textSize"] ?? 0,
            fontUi = (string?)current["fontUi"] ?? string.Empty,
            fontMono = (string?)current["fontMono"] ?? string.Empty,
            width = (string?)current["width"] ?? string.Empty,
            widthPercent = (int?)current["widthPercent"] ?? 0,
            opacity = (double?)paper?["opacity"] ?? 0,
            dim = (double?)paper?["dim"] ?? 0,
            focusX = (double?)paper?["focusX"] ?? 0.5,
            focusY = (double?)paper?["focusY"] ?? 0.5,
            muted = (bool?)paper?["muted"] ?? false,
            volume = (double?)paper?["volume"] ?? 1,
        };
        using var response = await _http.PostAsJsonAsync("/appearance", patch, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
    }

    /// <summary>Reads first-run setup for this session.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The setup state, or null for kernels without the endpoint.</returns>
    public async Task<ProviderSetup?> ProviderSetupAsync(CancellationToken token)
    {
        using var response = await _http.GetAsync("/provider-setup", token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<ProviderSetup>(response, token).ConfigureAwait(false);
    }

    /// <summary>Saves the provider key supplied during onboarding.</summary>
    /// <param name="apiKey">The key entered by the user.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The kernel acknowledgement.</returns>
    public Task SaveProviderKeyAsync(string apiKey, CancellationToken token)
    {
        var request = new
        {
            apiKey,
        };
        return PostAsync("/provider-setup", request, token);
    }

    /// <summary>Deserializes a required response.</summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="response">The successful response.</param>
    /// <param name="token">Cancels the read.</param>
    /// <returns>The entity.</returns>
    private static async Task<T> BodyAsync<T>(HttpResponseMessage response, CancellationToken token)
    {
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: token).ConfigureAwait(false)
            ?? throw new JsonException("Expected a JSON object from the kernel.");
    }

    /// <summary>Reads a typed JSON endpoint.</summary>
    /// <typeparam name="T">Response type.</typeparam>
    /// <param name="path">Endpoint relative to the session.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The response.</returns>
    private async Task<T> ReadAsync<T>(string path, CancellationToken token)
    {
        using var response = await _http.GetAsync(path, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<T>(response, token).ConfigureAwait(false);
    }

    /// <summary>Submits a command and checks the kernel acknowledgement.</summary>
    /// <param name="path">Endpoint relative to the session.</param>
    /// <param name="payload">The command arguments.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The acknowledgement.</returns>
    private async Task PostAsync(string path, object payload, CancellationToken token)
    {
        using var response = await _http.PostAsJsonAsync(path, payload, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
    }

    /// <summary>The host's models envelope.</summary>
    /// <param name="Models">Models available for the requested role.</param>
    private sealed record ModelCatalog(ModelChoice[]? Models);
}
