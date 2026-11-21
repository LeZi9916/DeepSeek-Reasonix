using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Reads local repository roots relative to this workspace.</summary>
    /// <param name="token">View lifetime.</param>
    /// <returns>The repository inventory.</returns>
    public Task<JsonObject> GitRepositoriesAsync(CancellationToken token) => ReadAsync<JsonObject>("/git/repos", token);

    /// <summary>Reads index and working-tree status including both sides of partially staged files.</summary>
    /// <param name="repo">Repository directory, empty for the workspace itself.</param>
    /// <param name="token">View lifetime.</param>
    /// <returns>The structured change list.</returns>
    public Task<JsonObject> GitChangesAsync(string repo, CancellationToken token) => ReadAsync<JsonObject>(GitRoute("/changes", repo), token);

    /// <summary>Reads the current repository's working diff.</summary>
    /// <param name="repo">Repository directory.</param>
    /// <param name="path">Path relative to the repository.</param>
    /// <param name="token">View lifetime.</param>
    /// <returns>The diff and truncation status.</returns>
    public Task<JsonObject> GitDiffAsync(string repo, string path, CancellationToken token) => ReadAsync<JsonObject>(GitRoute("/changes/diff", repo, ("path", path)), token);

    /// <summary>Reads the newest 100 commits, their parent edges and decorations.</summary>
    /// <param name="repo">Repository directory.</param>
    /// <param name="token">View lifetime.</param>
    /// <returns>The local history.</returns>
    public Task<JsonObject> GitHistoryAsync(string repo, CancellationToken token) => ReadAsync<JsonObject>(GitRoute("/workspace/git/log", repo, ("limit", "100")), token);

    /// <summary>Reads files in a commit or that commit's diff for an individual path.</summary>
    /// <param name="repo">Repository directory.</param>
    /// <param name="id">Selected commit object id.</param>
    /// <param name="path">Optional changed path.</param>
    /// <param name="token">View lifetime.</param>
    /// <returns>File list or patch.</returns>
    public Task<JsonObject> GitCommitDetailAsync(string repo, string id, string? path, CancellationToken token) =>
        ReadAsync<JsonObject>(path is null ? GitRoute("/git/commit", repo, ("id", id)) : GitRoute("/git/commit/diff", repo, ("id", id), ("path", path)), token);

    /// <summary>Stages or unstages the displayed set of paths.</summary>
    /// <param name="repo">Repository directory.</param>
    /// <param name="paths">Paths belonging to that repository, or null for every listed change.</param>
    /// <param name="unstage">Move out of the index while keeping local contents.</param>
    /// <param name="token">View lifetime.</param>
    /// <returns>The acknowledgement.</returns>
    public Task GitStageAsync(string repo, IReadOnlyList<string>? paths, bool unstage, CancellationToken token)
    {
        var request = GitFields(repo);
        request["unstage"] = unstage;
        if (paths is null)
        {
            request["all"] = true;
        }
        else
        {
            request["paths"] = System.Text.Json.JsonSerializer.SerializeToNode(paths);
        }

        return PostAsync("/changes/stage", request, token);
    }

    /// <summary>Discards one eligible file after explicit confirmation.</summary>
    /// <param name="repo">Repository directory.</param>
    /// <param name="path">Confirmed path.</param>
    /// <param name="token">View lifetime.</param>
    /// <returns>The acknowledgement.</returns>
    public Task GitDiscardAsync(string repo, string path, CancellationToken token)
    {
        var request = GitFields(repo);
        request["path"] = path;
        return PostAsync("/changes/discard", request, token);
    }

    /// <summary>Prepares the staged file list, sensitive-file flags, fingerprint and suggested message.</summary>
    /// <param name="repo">Repository directory.</param>
    /// <param name="token">Review lifetime.</param>
    /// <returns>The commit proposal.</returns>
    public Task<JsonObject> GitProposalAsync(string repo, CancellationToken token) =>
        DataPostAsync(GitRoute("/commit/propose", repo), new JsonObject(), token);

    /// <summary>Commits exactly the index fingerprint supplied by the reviewed proposal.</summary>
    /// <param name="repo">Repository directory.</param>
    /// <param name="fingerprint">Reviewed index fingerprint.</param>
    /// <param name="message">User-edited commit message.</param>
    /// <param name="acknowledge">Explicit sensitive-content acknowledgement.</param>
    /// <param name="token">View lifetime.</param>
    /// <returns>The resulting commit id and subject, or the kernel refusal.</returns>
    public Task<JsonObject> GitCommitAsync(string repo, string fingerprint, string message, bool acknowledge, CancellationToken token)
    {
        var request = GitFields(repo);
        request["fingerprint"] = fingerprint;
        request["message"] = message;
        request["acknowledgeSecrets"] = acknowledge;
        return DataPostAsync("/commit", request, token);
    }

    /// <summary>Reads the exact local branch and destination that a push would use.</summary>
    /// <param name="repo">Repository directory.</param>
    /// <param name="token">Review lifetime.</param>
    /// <returns>The kernel's unmodified push plan.</returns>
    public Task<JsonObject> GitPushPreviewAsync(string repo, CancellationToken token) => ReadAsync<JsonObject>(GitRoute("/git/push", repo), token);

    /// <summary>Submits the plan the user explicitly approved.</summary>
    /// <param name="repo">Repository directory.</param>
    /// <param name="plan">Unmodified plan from the same repo's preview.</param>
    /// <param name="token">View lifetime.</param>
    /// <returns>The result.</returns>
    public Task<JsonObject> GitPushAsync(string repo, JsonObject plan, CancellationToken token)
    {
        var request = GitFields(repo);
        request["plan"] = plan.DeepClone();
        return DataPostAsync("/git/push", request, token);
    }

    /// <summary>Lists the workspace-root repository's branches.</summary>
    /// <param name="token">View lifetime.</param>
    /// <returns>The branch catalog.</returns>
    public Task<JsonObject> GitBranchesAsync(CancellationToken token) => ReadAsync<JsonObject>("/workspace/branches", token);

    /// <summary>Checks out a local branch by its catalog name.</summary>
    /// <param name="branch">Selected branch.</param>
    /// <param name="token">View lifetime.</param>
    /// <returns>The resulting local repository state.</returns>
    public Task<JsonObject> GitSwitchBranchAsync(string branch, CancellationToken token) =>
        DataPostAsync("/workspace/branch/switch", new JsonObject { ["name"] = branch }, token);

    /// <summary>Builds a body scoped to its originating repository.</summary>
    /// <param name="repo">Workspace-relative repository directory.</param>
    /// <returns>The scoped request body.</returns>
    private static JsonObject GitFields(string repo) => repo.Length == 0 ? new JsonObject() : new JsonObject { ["repo"] = repo };

    /// <summary>Encodes every repository, commit and file query component independently.</summary>
    /// <param name="path">Kernel route.</param>
    /// <param name="repo">Workspace-relative repository directory.</param>
    /// <param name="fields">Additional query fields.</param>
    /// <returns>The route with its encoded query string.</returns>
    private static string GitRoute(string path, string repo, params (string Key, string Value)[] fields)
    {
        var query = fields.Select(p => p.Key + "=" + Uri.EscapeDataString(p.Value)).ToList();
        if (repo.Length > 0)
        {
            query.Add("repo=" + Uri.EscapeDataString(repo));
        }

        return query.Count == 0 ? path : path + "?" + string.Join("&", query);
    }
}
