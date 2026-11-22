using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Session;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>Checks native Git actions against a throwaway repository in a separately launched kernel.</summary>
public sealed class GitWorkspaceTests
{
    /// <summary>Creates a real isolated worktree from a branch and reads the new root.</summary>
    /// <returns>The temporary-repository worktree check.</returns>
    [Fact]
    public async Task IsolatedWorktreeCreatesBranchCopy()
    {
        var binary = HostLauncher.Locate();
        if (binary is null)
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("STUDIO_TEST_HOST_REQUIRED"));
            return;
        }

        var scratch = Directory.CreateTempSubdirectory("studio-worktree-");
        try
        {
            var root = scratch.CreateSubdirectory("project");
            var hooks = scratch.CreateSubdirectory("empty-hooks");
            var profile = scratch.CreateSubdirectory("profile");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var token = timeout.Token;
            await GitAsync(root, hooks, token, "init", "-q", "-b", "main");
            await GitAsync(root, hooks, token, "config", "core.autocrlf", "false");
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "README.md"), "worktree fixture\n", token);
            await GitAsync(root, hooks, token, "add", "--", "README.md");
            await GitAsync(root, hooks, token, "commit", "-qm", "baseline");
            await GitAsync(root, hooks, token, "branch", "topic");

            using var kernel = await HostLauncher.StartAsync(binary, Array.Empty<string>(), token, profile.FullName);
            await using var initial = new SessionEngine(kernel);
            await using var engine = await initial.OpenProjectAsync(root.FullName, token);
            var api = engine.Pages;
            var inventory = await api.WorkspaceInventoryAsync(token);
            Assert.True(inventory.CanIsolate, "host did not offer worktree isolation");
            var sources = await api.WorktreeSourcesAsync(token);
            Assert.True(sources.Available);
            Assert.Contains(sources.Sources, source => source.Ref == "refs/heads/topic");
            var isolated = await api.IsolateWorkspaceAsync(root.FullName, "refs/heads/topic", [], token);
            Assert.NotEqual(root.FullName, isolated);
            Assert.True(Directory.Exists(isolated), isolated);
            Assert.True(File.Exists(Path.Combine(isolated, "README.md")));
            var status = await engine.StatusAsync(token);
            Assert.Equal(isolated, status!.WorkspaceRoot);
            await using var remounted = await initial.OpenProjectAsync(isolated, token);
            var remountedStatus = await remounted.StatusAsync(token);
            Assert.Equal(isolated, remountedStatus!.WorkspaceRoot);
        }
        finally
        {
            foreach (var file in scratch.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                file.Attributes &= ~FileAttributes.ReadOnly;
            }

            scratch.Delete(true);
        }
    }

    /// <summary>Exercises partial staging, index reset, discard and historical file patches.</summary>
    /// <returns>The temporary-repository check.</returns>
    [Fact]
    public async Task NestedRepoIndexHistoryAndRestoreReachGit()
    {
        var binary = HostLauncher.Locate();
        if (binary is null)
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("STUDIO_TEST_HOST_REQUIRED"));
            return;
        }

        var scratch = Directory.CreateTempSubdirectory("studio-git-");
        try
        {
            var root = scratch.CreateSubdirectory("project");
            var repo = root.CreateSubdirectory("client");
            var hooks = scratch.CreateSubdirectory("empty-hooks");
            var profile = scratch.CreateSubdirectory("profile");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var token = timeout.Token;
            await GitAsync("init", "-q", "-b", "main");
            await GitAsync("config", "core.autocrlf", "false");
            var path = "notes with spaces.txt";
            var file = Path.Combine(repo.FullName, path);
            await File.WriteAllTextAsync(file, "original\n", token);
            await GitAsync("add", "--", path);
            await GitAsync("commit", "-qm", "baseline");

            using var kernel = await HostLauncher.StartAsync(binary, Array.Empty<string>(), token, profile.FullName);
            await using var initial = new SessionEngine(kernel);
            await using var engine = await initial.OpenProjectAsync(root.FullName, token);
            var api = engine.Pages;
            var repos = await api.GitRepositoriesAsync(token);
            Assert.True(repos["repos"]!.AsArray().Any(r => (string?)r?["dir"] == "client"), repos.ToJsonString());
            var history = await api.GitHistoryAsync("client", token);
            var id = (string)Assert.Single(history["commits"]!.AsArray())!["id"]!;
            var files = await api.GitCommitDetailAsync("client", id, null, token);
            Assert.Equal(path, (string?)Assert.Single(files["files"]!.AsArray())!["path"]);
            Assert.Contains("+original", (string?)(await api.GitCommitDetailAsync("client", id, path, token))["diff"]);
            await File.WriteAllTextAsync(file, "index version\n", token);
            await api.GitStageAsync("client", [path], false, token);
            await File.WriteAllTextAsync(file, "worktree version\n", token);
            var entries = (await api.GitChangesAsync("client", token))["changes"]!.AsArray();
            var partial = Assert.Single(entries)!;
            Assert.True((bool?)partial["staged"]);
            Assert.True((bool?)partial["unstaged"]);
            await api.GitStageAsync("client", [path], true, token);
            Assert.Contains("+worktree version", (string?)(await api.GitDiffAsync("client", path, token))["diff"]);
            await api.GitDiscardAsync("client", path, token);
            Assert.Equal("original\n", await File.ReadAllTextAsync(file, token));
            Assert.Empty((await api.GitChangesAsync("client", token))["changes"]?.AsArray() ?? []);
            await GitAsync("branch", "alternate");
            await using var repoEngine = await initial.OpenProjectAsync(repo.FullName, token);
            var branches = await repoEngine.Pages.GitBranchesAsync(token);
            Assert.Contains(branches["branches"]!.AsArray(), b => (string?)b?["name"] == "alternate");
            var switched = await repoEngine.Pages.GitSwitchBranchAsync("alternate", token);
            Assert.Equal("alternate", (string?)switched["branch"]);

            async Task GitAsync(params string[] args)
            {
                var start = new ProcessStartInfo("git") { UseShellExecute = false, WorkingDirectory = repo.FullName, RedirectStandardError = true };
                foreach (var arg in new[] { "-c", "user.name=Studio Test", "-c", "user.email=fixture@localhost", "-c", "core.autocrlf=false", "-c", "commit.gpgsign=false", "-c", "core.hooksPath=" + hooks.FullName }.Concat(args))
                {
                    start.ArgumentList.Add(arg);
                }

                using var git = Process.Start(start)!;
                var stderr = await git.StandardError.ReadToEndAsync(token);
                await git.WaitForExitAsync(token);
                Assert.True(git.ExitCode == 0, stderr);
            }
        }
        finally
        {
            // Git's loose objects can carry read-only attributes on Windows.
            foreach (var f in scratch.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                f.Attributes &= ~FileAttributes.ReadOnly;
            }

            scratch.Delete(true);
        }
    }

    /// <summary>Runs one git command in a fixture repository.</summary>
    /// <param name="root">Repository directory.</param>
    /// <param name="hooks">Empty hooks directory.</param>
    /// <param name="token">Cancellation token.</param>
    /// <param name="args">Git arguments.</param>
    /// <returns>The completed command.</returns>
    private static async Task GitAsync(DirectoryInfo root, DirectoryInfo hooks, CancellationToken token, params string[] args)
    {
        var start = new ProcessStartInfo("git") { UseShellExecute = false, WorkingDirectory = root.FullName, RedirectStandardError = true };
        foreach (var arg in new[] { "-c", "user.name=Studio Test", "-c", "user.email=fixture@localhost", "-c", "core.autocrlf=false", "-c", "commit.gpgsign=false", "-c", "core.hooksPath=" + hooks.FullName }.Concat(args))
        {
            start.ArgumentList.Add(arg);
        }

        using var git = Process.Start(start)!;
        var stderr = await git.StandardError.ReadToEndAsync(token);
        await git.WaitForExitAsync(token);
        Assert.True(git.ExitCode == 0, stderr);
    }
}
