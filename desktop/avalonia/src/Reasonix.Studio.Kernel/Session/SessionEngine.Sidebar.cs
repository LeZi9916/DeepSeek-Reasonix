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
        var descriptor = await Pages.OpenRemoteRuntimeAsync(host, path, session, preset, token).ConfigureAwait(false);
        return CreateRuntime(descriptor);
    }

    /// <summary>Identifies the initial host connection without creating another runtime.</summary>
    /// <param name="status">The current session identity.</param><param name="token">Cancels identification.</param>
    /// <returns>The acknowledged initial runtime identity.</returns>
    public async Task IdentifyRuntimeAsync(SessionStatus status, CancellationToken token)
    {
        if (Runtime is not null || !CanOpenProjects)
        {
            return;
        }

        var runtimes = await Pages.RuntimesAsync(token).ConfigureAwait(false);
        var found = runtimes.FirstOrDefault(rt => rt.Host.Length == 0 && rt.Root == status.WorkspaceRoot && rt.SessionPath == status.SessionPath);
        if (found is not null)
        {
            ValidateRuntime(found);
            Runtime = found;
        }
    }

    /// <summary>Validates the host-issued runtime path before a client may address it.</summary>
    /// <param name="runtime">Canonical runtime descriptor.</param>
    private static void ValidateRuntime(RuntimeDescriptor runtime)
    {
        if (runtime.Id.Length < 2 || runtime.Id[0] != 'r' || !runtime.Id[1..].All(char.IsAsciiDigit) || runtime.Base != "/rt/" + runtime.Id)
        {
            throw new System.IO.InvalidDataException("Invalid workspace runtime base.");
        }
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
        return new SessionEngine(host, "/rt/" + id, remote.Length > 0) { RemoteHost = remote, Runtime = System.Text.Json.JsonSerializer.Deserialize<RuntimeDescriptor>(descriptor.ToJsonString()) };
    }

    /// <summary>Constructs a validated runtime over the launch or its explicitly supplied factory.</summary>
    /// <param name="descriptor">The hub-issued runtime identity.</param><returns>The corresponding client engine.</returns>
    private SessionEngine CreateRuntime(System.Text.Json.Nodes.JsonObject descriptor)
    {
        var runtime = System.Text.Json.JsonSerializer.Deserialize<RuntimeDescriptor>(descriptor.ToJsonString())!;
        ValidateRuntime(runtime);
        return _kernel is { } kernel
            ? EngineForRuntime(kernel, descriptor)
            : _runtimeFactory?.Invoke(runtime) ?? throw new InvalidOperationException("This engine has no running workspace host.");
    }
}
