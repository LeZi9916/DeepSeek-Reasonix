using Reasonix.Studio.Kernel.Client;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Session;

public sealed partial class SessionEngine
{
    /// <summary>Gets the machine identity carried by the hub-issued runtime.</summary>
    public string RemoteHost
    {
        get; private init;
    } = string.Empty;

    /// <summary>Gets the draft authority, including the remote machine when one owns the pane.</summary>
    public string DraftHost => RemoteHost.Length == 0 ? _host : System.Text.Json.JsonSerializer.Serialize(new[] { _host, RemoteHost });

    /// <summary>Opens a remote project through the same launch and runtime boundary.</summary>
    /// <param name="host">Saved machine name.</param><param name="path">Remote workspace.</param>
    /// <param name="token">Cancels opening.</param><param name="session">Saved transcript.</param>
    /// <param name="preset">Explicit composition for a new task.</param>
    /// <returns>The runtime's independent engine.</returns>
    public async Task<SessionEngine> OpenRemoteProjectAsync(string host, string path, CancellationToken token, string? session = null, string? preset = null)
    {
        var kernel = _kernel ?? throw new InvalidOperationException("This engine has no running workspace host.");
        var descriptor = await Pages.OpenRemoteRuntimeAsync(host, path, session, preset, token).ConfigureAwait(false);
        return EngineForRuntime(kernel, descriptor);
    }

    /// <summary>Validates the returned same-origin runtime before constructing its engine.</summary>
    /// <param name="host">Authenticated launch.</param><param name="descriptor">The hub-issued runtime.</param>
    /// <returns>An independently disposable engine.</returns>
    private static SessionEngine EngineForRuntime(LaunchedKernel host, System.Text.Json.Nodes.JsonObject descriptor)
    {
        var id = (string?)descriptor["id"];
        if (string.IsNullOrEmpty(id) || id.Length < 2 || id[0] != 'r' || !id[1..].All(char.IsAsciiDigit) || (string?)descriptor["base"] != "/rt/" + id)
        {
            throw new System.IO.InvalidDataException("Invalid workspace runtime base.");
        }

        var remote = (string?)descriptor["host"] ?? string.Empty;
        return new SessionEngine(host, "/rt/" + id, remote.Length > 0) { RemoteHost = remote };
    }
}
