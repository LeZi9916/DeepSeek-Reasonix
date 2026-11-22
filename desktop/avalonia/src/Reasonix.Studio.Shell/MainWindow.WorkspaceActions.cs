using Avalonia.Controls;
using Avalonia.Input.Platform;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell.Host;
using Reasonix.Studio.Shell.Workspace;
using System;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The explicit edit or confirmation overlay.</summary>
    private WorkspaceActionPrompt? _workspacePrompt;

    /// <summary>Waits for the user's concrete edit before submitting a canonical mutation.</summary>
    /// <param name="title">Operation label.</param><param name="target">Canonical target.</param>
    /// <param name="value">Editable text, or null for confirmation.</param><returns>The accepted value.</returns>
    private Task<string?> AskWorkspaceAsync(string title, string target, string? value = null)
    {
        if (ScaledWindow.Child is not Grid host)
        {
            return Task.FromResult<string?>(null);
        }

        if (_workspacePrompt is null)
        {
            _workspacePrompt = new WorkspaceActionPrompt();
            _workspacePrompt.ZIndex = 1100;
            host.Children.Add(_workspacePrompt);
        }

        return _workspacePrompt.Ask(title, target, value, Language);
    }

    /// <summary>Routes sidebar actions through the existing canonical hub APIs.</summary>
    /// <param name="action">The actual menu operation and target.</param><returns>The acknowledged operation.</returns>
    private async Task RunWorkspaceActionAsync(WorkspaceAction action)
    {
        if (_engine is not { } engine)
        {
            return;
        }

        var target = action.Target;
        var row = action.Session;
        var body = new JsonObject { ["path"] = target.SessionPath.Length > 0 ? target.SessionPath : target.WorkspaceRoot };
        var operation = string.Empty;
        try
        {
            if (await RunSidebarGestureAsync(action).ConfigureAwait(true))
            {
                return;
            }

            if (target.Host.Length > 0 && action.Action is "session.rename-start" or "session.auto-name" or "session.export" or "session.export-md" or "session.move-to-start" or "session.move-up" or "session.move-down")
            {
                throw new InvalidOperationException(MessageCatalog.Get("settings.not_in_this_version_yet", Language));
            }

            switch (action.Action)
            {
                case "workspace.open-with":
                    await (_launchOwner ?? engine).Pages.OpenWorkspaceWithAsync(target.WorkspaceRoot, action.Value, _closing.Token).ConfigureAwait(true);
                    return;
                case "remote-host.move":
                    await (_launchOwner ?? engine).Pages.PlaceRemoteHostAsync(target.Host, action.Value, _closing.Token).ConfigureAwait(true);
                    await LoadTreeAsync().ConfigureAwait(true);
                    return;
                case "session.branch":
                    if (row is not { RuntimeId.Length: > 0 } || target.Host.Length > 0)
                    {
                        return;
                    }

                    var turns = (await engine.Pages.RuntimeCheckpointsAsync(row.RuntimeId, _closing.Token).ConfigureAwait(true)).Select(checkpoint => (int?)checkpoint?["turn"]).OfType<int>().Order().ToArray();
                    var active = _navigation?.Panes.FirstOrDefault(p => p.Engine.Runtime?.Id == row.RuntimeId);
                    var live = active?.Engine.State.Running ?? Projects.IsLive(target);
                    var finished = live ? turns.SkipLast(1).ToArray() : turns;
                    if (finished.Length == 0)
                    {
                        throw new InvalidOperationException(MessageCatalog.Get("session.there_is_no_finished_reply", Language));
                    }

                    var branch = await engine.Pages.BranchRuntimeAsync(row.RuntimeId, finished[^1], (row.Title.Length > 0 ? row.Title : row.Name) + " · " + MessageCatalog.Get("common.branch", Language), _closing.Token).ConfigureAwait(true);
                    await OpenTargetAsync(target with
                    {
                        SessionPath = branch,
                    }).ConfigureAwait(true);
                    return;
                case "workspace.reveal":
                    var refusal = await new AvaloniaHost(this).RevealWorkspaceAsync(target.WorkspaceRoot).ConfigureAwait(true);
                    if (refusal is not null)
                    {
                        throw new InvalidOperationException(refusal.Error);
                    }

                    return;
                case "workspace.copy-path":
                    if (Clipboard is { } copyPath)
                    {
                        await copyPath.SetTextAsync(target.WorkspaceRoot).ConfigureAwait(true);
                    }

                    return;
                case "session.rename-start":
                    var title = await AskWorkspaceAsync(MessageCatalog.Get("common.renamed", Language), target.SessionPath, row?.Title ?? string.Empty).ConfigureAwait(true);
                    if (title is null || string.IsNullOrWhiteSpace(title))
                    {
                        return;
                    }

                    body["title"] = title.Trim();
                    operation = "sessions/rename";
                    break;
                case "session.auto-name":
                    operation = "sessions/auto-name";
                    break;
                case "session.move-up":
                case "session.move-down":
                    body["direction"] = action.Action == "session.move-up" ? -1 : 1;
                    operation = "sessions/move";
                    break;
                case "session.move-to-start":
                    OpenWorkspaceMove(action);
                    return;
                case "session.copy-info":
                    if (Clipboard is { } clipboard)
                    {
                        await clipboard.SetTextAsync(string.Join(Environment.NewLine, row?.Title ?? row?.Name ?? string.Empty, target.Host, target.WorkspaceRoot, target.SessionPath)).ConfigureAwait(true);
                    }

                    return;
                case "session.export":
                case "session.export-md":
                    var exported = await engine.Pages.WorkspaceActionAsync("sessions/export", body, _closing.Token).ConfigureAwait(true);
                    if (exported is not null)
                    {
                        var content = (string?)exported["content"] ?? string.Empty;
                        var markdown = action.Action == "session.export-md";
                        if (markdown)
                        {
                            content = ExportMarkdown(content, row?.Title ?? row?.Name ?? string.Empty);
                        }

                        await new AvaloniaHost(this).SaveTextAsync((row?.Name.Length > 0 ? row.Name : "reasonix-session") + (markdown ? ".md" : ".json"), content).ConfigureAwait(true);
                    }

                    return;
                case "session.pause":
                    if (row?.RuntimeId.Length > 0)
                    {
                        await engine.Pages.WorkspaceRuntimeActionAsync(row.RuntimeId, false, _closing.Token).ConfigureAwait(true);
                    }

                    return;
                case "session.close":
                    var pane = _navigation?.Panes.FirstOrDefault(p => p.Host == target.Host && p.Status.SessionPath == target.SessionPath);
                    if (pane is not null)
                    {
                        await ClosePanesAsync(new[] { pane }).ConfigureAwait(true);
                    }
                    else if (row?.RuntimeId.Length > 0)
                    {
                        await engine.Pages.WorkspaceRuntimeActionAsync(row.RuntimeId, true, _closing.Token).ConfigureAwait(true);
                    }

                    return;
                case "session.delete":
                case "workspace.remove":
                    if (await AskWorkspaceAsync(MessageCatalog.Get(action.Action == "session.delete" ? "common.delete_session" : "common.remove_from_the_list", Language), (string)body["path"]!).ConfigureAwait(true) is null)
                    {
                        return;
                    }

                    if (target.Host.Length > 0)
                    {
                        await engine.Pages.RemoveRemoteWorkspaceItemAsync(target.Host, target.SessionPath.Length > 0 ? "sessions" : "workspaces", (string)body["path"]!, _closing.Token).ConfigureAwait(true);
                    }
                    else
                    {
                        operation = action.Action == "session.delete" ? "sessions/remove" : "workspaces/remove";
                    }

                    break;
                case "workspace.move-up":
                case "workspace.move-down":
                    body["direction"] = action.Action == "workspace.move-up" ? -1 : 1;
                    operation = "workspaces/move";
                    break;
                default:
                    return;
            }

            if (operation.Length > 0)
            {
                await engine.Pages.WorkspaceActionAsync(operation, body, _closing.Token).ConfigureAwait(true);
            }

            await LoadTreeAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            ShowRefusal(error);
        }
    }

    /// <summary>Exports provider messages while excluding tool and host-authored records.</summary>
    /// <param name="content">Native archive or JSON lines.</param><param name="title">Conversation title.</param>
    /// <returns>The Markdown reading transcript.</returns>
    private string ExportMarkdown(string content, string title)
    {
        var output = new StringBuilder("# " + title + "\n\n");
        JsonNode[] records;
        try
        {
            records = JsonNode.Parse(content)?["records"]?.AsArray().OfType<JsonNode>().ToArray() ?? Array.Empty<JsonNode>();
        }
        catch (System.Text.Json.JsonException)
        {
            records = content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!).ToArray();
        }

        foreach (var record in records)
        {
            var role = (string?)record["role"];
            var text = (string?)record["content"];
            if (role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(text) || (role == "user" && (bool?)record["host_authored"] == true))
            {
                continue;
            }

            output.Append("## ").Append(MessageCatalog.Get(role == "user" ? "data.me" : "data.assistant", Language)).Append("\n\n").Append(text.Trim()).Append("\n\n");
        }

        return output.ToString();
    }
}
