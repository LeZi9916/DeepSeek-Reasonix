using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Reads usage for a duration or a custom date range.</summary>
    /// <param name="days">Days to include.</param>
    /// <param name="from">Inclusive first day for a custom period.</param>
    /// <param name="to">Inclusive last day.</param>
    /// <param name="token">Window lifetime.</param>
    /// <returns>Kernel usage totals and breakdowns.</returns>
    public Task<JsonObject> UsageAsync(int days, string? from, string? to, CancellationToken token) =>
        ReadAsync<JsonObject>("/usage?" + (from is null ? "days=" + days : "from=" + Uri.EscapeDataString(from) + "&to=" + Uri.EscapeDataString(to ?? from)), token);

    /// <summary>Reads thirty days of upload traffic.</summary>
    /// <param name="token">Window lifetime.</param>
    /// <returns>Daily and per-provider upload totals.</returns>
    public Task<JsonObject> UploadsAsync(CancellationToken token) => ReadAsync<JsonObject>("/usage/upload?days=30", token);

    /// <summary>Reads or changes the display currency.</summary>
    /// <param name="mode">Null for a read, otherwise the selected mode.</param>
    /// <param name="token">Window lifetime.</param>
    /// <returns>The accepted display preference.</returns>
    public Task<JsonObject> CurrencyAsync(string? mode, CancellationToken token) => mode is null ? ReadAsync<JsonObject>("/display-currency", token) : DataPostAsync("/display-currency", new JsonObject { ["mode"] = mode }, token);

    /// <summary>Reads the storage inventory and its measured sizes.</summary>
    /// <param name="token">Window lifetime.</param>
    /// <returns>Roots and drive totals.</returns>
    public Task<JsonObject> StorageAsync(CancellationToken token) => ReadAsync<JsonObject>("/storage", token);

    /// <summary>Plans or confirms moving a storage root.</summary>
    /// <param name="root">Root from the inventory.</param>
    /// <param name="dir">Destination selected by the reader.</param>
    /// <param name="apply">True after the plan was reviewed.</param>
    /// <param name="token">Window lifetime.</param>
    /// <returns>The move plan or outcome.</returns>
    public Task<JsonObject> MoveStorageAsync(string root, string dir, bool apply, CancellationToken token) =>
        DataPostAsync(apply ? "/storage/move" : "/storage/plan", new JsonObject { ["root"] = root, ["dir"] = dir }, token);

    /// <summary>Imports a file the user selected using the hub's native archive decoder.</summary>
    /// <param name="file">User-granted input stream.</param>
    /// <param name="root">The receiving project.</param>
    /// <param name="token">Window lifetime.</param>
    /// <returns>The imported transcript location.</returns>
    public async Task<JsonObject> ImportSessionAsync(System.IO.Stream file, string root, CancellationToken token)
    {
        using var content = new StreamContent(file);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        using var response = await _http.PostAsync("/tree/sessions/import-native?workspace=" + Uri.EscapeDataString(root), content, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
    }

    /// <summary>Imports an explicitly selected older data directory into the current project.</summary>
    /// <param name="path">Selected directory.</param>
    /// <param name="root">Receiving workspace.</param>
    /// <param name="token">Window lifetime.</param>
    /// <returns>Import summary and any skips.</returns>
    public Task<JsonObject> ImportLegacyAsync(string path, string root, CancellationToken token) =>
        DataPostAsync("/tree/sessions/import-legacy", new JsonObject { ["path"] = path, ["workspace"] = root }, token);

    /// <summary>Opens an isolated worktree through the kernel.</summary>
    /// <param name="token">Window lifetime.</param>
    /// <returns>The acknowledgement.</returns>
    public Task IsolateAsync(CancellationToken token) => PostAsync("/workspace", new JsonObject { ["isolate"] = true }, token);

    /// <summary>Reads the backup categories and minimum passphrase length.</summary>
    /// <param name="token">Window lifetime.</param>
    /// <returns>The export catalog.</returns>
    public Task<JsonObject> BackupCatalogAsync(CancellationToken token) => ReadAsync<JsonObject>("/backups/file", token);

    /// <summary>Requests an encrypted configuration export, import preview or accepted restore.</summary>
    /// <param name="operation">One of export, preview and apply.</param>
    /// <param name="fields">Fields selected by the reader.</param>
    /// <param name="token">Window lifetime.</param>
    /// <returns>The result from the backup service.</returns>
    public Task<JsonObject> BackupFileAsync(string operation, JsonObject fields, CancellationToken token)
    {
        if (operation is not ("export" or "preview" or "apply"))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        return DataPostAsync("/backups/file/" + operation, fields, token);
    }

    /// <summary>Posts a JSON action and checks its response.</summary>
    /// <param name="path">Kernel-relative route.</param>
    /// <param name="fields">Action fields.</param>
    /// <param name="token">Window lifetime.</param>
    /// <returns>The response body.</returns>
    private async Task<JsonObject> DataPostAsync(string path, JsonObject fields, CancellationToken token)
    {
        using var response = await _http.PostAsJsonAsync(path, fields, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
    }
}
