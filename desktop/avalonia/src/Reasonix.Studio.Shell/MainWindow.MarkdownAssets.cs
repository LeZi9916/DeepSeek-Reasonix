using Avalonia.Media.Imaging;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages;
using Reasonix.Studio.Shell.Workbench;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Connects Markdown references to existing workspace file operations.</summary>
    private void InitializeMarkdownAssets() => Conversation.WorkspaceAssets = new MarkdownAssets(
        ResolveMarkdownPath,
        path => OpenEntryAsync(new WorkspaceFileEntry(path, false)),
        ReadMarkdownImageAsync);

    /// <summary>Normalizes a file destination in the kernel workspace's path dialect.</summary>
    /// <param name="destination">The explicit Markdown link or image source.</param>
    /// <returns>The relative workspace path, when the destination names one.</returns>
    private string? ResolveMarkdownPath(string destination)
    {
        if (_workspaceRoot.Length == 0 || destination.Length == 0 || destination[0] == '#')
        {
            return null;
        }

        var fragment = destination.IndexOf('#');
        var path = Uri.UnescapeDataString(fragment < 0 ? destination : destination[..fragment]).Replace('\\', '/');
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            return null;
        }

        if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && uri is { IsFile: true })
        {
            path = uri.LocalPath.Replace('\\', '/');
        }

        var root = _workspaceRoot.Replace('\\', '/').TrimEnd('/');
        var absolute = path.StartsWith('/') || (path.Length > 2 && path[1] == ':' && path[2] == '/');
        if (absolute)
        {
            var compare = root.Length > 1 && root[1] == ':' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!path.StartsWith(root + '/', compare))
            {
                return null;
            }

            path = path[(root.Length + 1)..];
        }

        var parts = new List<string>();
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "..")
            {
                if (parts.Count == 0)
                {
                    return null;
                }

                parts.RemoveAt(parts.Count - 1);
            }
            else if (part != ".")
            {
                parts.Add(part);
            }
        }

        return parts.Count > 0 ? string.Join('/', parts) : null;
    }

    /// <summary>Loads only image previews admitted by the owning kernel runtime.</summary>
    /// <param name="path">The resolved workspace path.</param><param name="token">The rendered block lifetime.</param>
    /// <returns>The image or an alternate-text fallback.</returns>
    private async Task<Bitmap?> ReadMarkdownImageAsync(string path, CancellationToken token)
    {
        if (_engine is not { } owner)
        {
            return null;
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _closing.Token);
        try
        {
            var info = await owner.Pages.PreviewInfoAsync(path, lifetime.Token).ConfigureAwait(true);
            if ((string?)info["kind"] != "image" || (long?)info["size"] > (long?)info["limit"] || !ReferenceEquals(_engine, owner))
            {
                return null;
            }

            var bytes = await owner.Pages.PreviewContentAsync(path, (string?)info["version"] ?? string.Empty, lifetime.Token).ConfigureAwait(true);
            if (bytes is null || !ReferenceEquals(_engine, owner))
            {
                return null;
            }

            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception error) when (error is KernelRefusal or HttpRequestException or IOException or ArgumentException
            || (error is OperationCanceledException && lifetime.IsCancellationRequested))
        {
            return null;
        }
    }
}
