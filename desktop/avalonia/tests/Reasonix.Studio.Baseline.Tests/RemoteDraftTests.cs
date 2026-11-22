using Reasonix.Studio.Kernel.Boundary;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages.Composer;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>Draft identity follows the machine returned in the canonical runtime descriptor.</summary>
public sealed class RemoteDraftTests
{
    /// <summary>Equal workspace and session paths on different machines never share a draft.</summary>
    /// <returns>The isolated runtime engines.</returns>
    [Fact]
    public async Task HostNamesSeparateOtherwiseIdenticalDrafts()
    {
        using var process = new Process();
        var launch = new LaunchedKernel(process, new LaunchHandshake(new Uri("http://127.0.0.1:44000"), new string('a', 64)));
        var factory = typeof(SessionEngine).GetMethod("EngineForRuntime", BindingFlags.NonPublic | BindingFlags.Static)!;
        SessionEngine Open(string machine) => (SessionEngine)factory.Invoke(null, new object[]
        {
            launch,
            new JsonObject { ["id"] = "r1", ["base"] = "/rt/r1", ["host"] = machine },
        })!;
        await using var gpu = Open("gpu-box");
        await using var builder = Open("builder");
        await using var local = Open(string.Empty);
        Assert.Equal(gpu.Host, builder.Host);
        Assert.NotEqual(gpu.DraftHost, builder.DraftHost);
        Assert.NotEqual(gpu.DraftHost, local.DraftHost);
        Assert.Equal(local.Host, local.DraftHost);
        Assert.NotEqual(DraftStore.KeyFor(gpu.DraftHost, "/work", "/same.jsonl"), DraftStore.KeyFor(builder.DraftHost, "/work", "/same.jsonl"));
        Assert.Equal("gpu-box", gpu.RemoteHost);
        Assert.True(gpu.IsRemote);
        Assert.False(local.IsRemote);
    }
}
