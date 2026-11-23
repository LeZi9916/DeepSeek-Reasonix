using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Browses directories without inferring path syntax on the local machine.</summary>
public sealed partial class RemoteDirectoryPicker : SettingsFormView
{
    /// <summary>The active picker's answer.</summary>
    private TaskCompletionSource<string?>? _selection;

    /// <summary>The directory read for the active machine.</summary>
    private Func<string, Task>? _read;

    /// <summary>The last path acknowledged by the machine.</summary>
    private string? _current;

    /// <summary>The parent path supplied by the machine.</summary>
    private string? _parent;

    /// <summary>Initializes a new instance of the <see cref="RemoteDirectoryPicker"/> class.</summary>
    public RemoteDirectoryPicker()
    {
        InitializeComponent();
        Veil.Tapped += (_, _) => _selection?.TrySetResult(null);
        Cancel.Click += (_, _) => _selection?.TrySetResult(null);
        Choose.Click += (_, _) => _selection?.TrySetResult(_current);
        PathInput.TextChanged += (_, _) => RefreshActions();
        Up.Click += async (_, _) =>
        {
            if (_read is { } read && _parent is { } parent)
            {
                await read(parent).ConfigureAwait(true);
            }
        };
        Go.Click += async (_, _) => await FollowDraftAsync().ConfigureAwait(true);
        AddHandler(
            KeyDownEvent,
            (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    _selection?.TrySetResult(null);
                }
            },
            RoutingStrategies.Tunnel);
        PathInput.AddHandler(
            KeyDownEvent,
            async (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    await FollowDraftAsync().ConfigureAwait(true);
                }
            },
            RoutingStrategies.Tunnel);
    }

    /// <summary>Opens a picker for a saved machine and returns the chosen directory.</summary>
    /// <param name="api">The host that owns the machine book.</param><param name="host">The saved machine.</param>
    /// <param name="start">The initial directory, or empty for the login home.</param>
    /// <param name="language">The reader's language.</param><param name="describe">Formats endpoint failures.</param>
    /// <param name="token">The settings lifetime.</param><returns>The chosen canonical path, or null on cancellation.</returns>
    internal async Task<string?> PickAsync(StudioPageClient api, string host, string start, InterfaceLanguage language, Func<Exception, string> describe, CancellationToken token)
    {
        var selection = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var calls = CancellationTokenSource.CreateLinkedTokenSource(token);
        var browsing = calls.Token;
        using var registration = token.Register(() => selection.TrySetResult(null));
        _selection = selection;
        _read = LoadAsync;
        Translate(language);
        Heading.Text = Text("remote.pick_a_folder_on_host", ("host", host));
        AutomationProperties.SetName(Card, Heading.Text);
        AutomationProperties.SetName(Up, Text("remote.up_one_level"));
        ToolTip.SetTip(Up, Text("remote.up_one_level"));
        _current = _parent = null;
        PathInput.Text = start;
        Rows.IsEnabled = true;
        Rows.Children.Clear();
        Rows.Children.Add(ListingStatus);
        ListingStatus.Text = Text("remote.connecting");
        ListingStatus.IsVisible = true;
        Unlisted.IsVisible = Truncated.IsVisible = false;
        IsVisible = true;
        PathInput.Focus();
        _ = LoadAsync(start);
        try
        {
            return await selection.Task.ConfigureAwait(true);
        }
        finally
        {
            calls.Cancel();
            _selection = null;
            _read = null;
            IsVisible = false;
        }

        async Task LoadAsync(string path)
        {
            if (browsing.IsCancellationRequested || !Rows.IsEnabled)
            {
                return;
            }

            Failure.IsVisible = false;
            Rows.IsEnabled = false;
            Results.Opacity = 0.55;
            RefreshActions();
            try
            {
                var listing = await api.RemoteDirectoriesAsync(host, path, browsing).ConfigureAwait(true);
                if (!browsing.IsCancellationRequested)
                {
                    ShowListing(listing);
                }
            }
            catch (Exception error)
            {
                if (!browsing.IsCancellationRequested)
                {
                    Failure.Text = describe(error);
                    Failure.IsVisible = true;
                    ListingStatus.IsVisible = false;
                }
            }
            finally
            {
                if (!browsing.IsCancellationRequested)
                {
                    Rows.IsEnabled = true;
                    Results.Opacity = 1;
                    RefreshActions();
                }
            }
        }
    }

    /// <summary>Projects the canonical path and its reported child directories.</summary>
    /// <param name="listing">The machine's directory reply.</param>
    private void ShowListing(JsonObject listing)
    {
        _current = (string?)listing["path"];
        _parent = (string?)listing["parent"];
        PathInput.Text = _current;
        Rows.Children.Clear();
        Rows.Children.Add(ListingStatus);
        var folders = listing["folders"]?.AsArray().OfType<JsonObject>() ?? [];
        foreach (var entry in folders)
        {
            var name = (string?)entry["name"];
            var path = (string?)entry["path"];
            var row = new Button { Content = name, Classes = { "folder-row" } };
            AutomationProperties.SetName(row, name);
            AutomationProperties.SetAutomationId(row, "action:remote.directory.open");
            row.Click += async (_, _) =>
            {
                if (_read is { } read && path is not null)
                {
                    await read(path).ConfigureAwait(true);
                }
            };
            Rows.Children.Add(row);
        }

        Unlisted.IsVisible = (bool?)listing["unlisted"] == true;
        Truncated.IsVisible = (bool?)listing["truncated"] == true;
        ListingStatus.Text = Text("remote.nothing_below_this_folder_it");
        ListingStatus.IsVisible = Rows.Children.Count == 1 && !Unlisted.IsVisible;
    }

    /// <summary>Enables actions only when their acknowledged target is available.</summary>
    private void RefreshActions()
    {
        Up.IsEnabled = Rows.IsEnabled && !string.IsNullOrEmpty(_parent);
        Choose.IsEnabled = Rows.IsEnabled && !string.IsNullOrEmpty(_current);
        Go.IsEnabled = Rows.IsEnabled && !string.IsNullOrWhiteSpace(PathInput.Text);
    }

    /// <summary>Submits the typed path without treating it as a selection.</summary>
    /// <returns>The directory read.</returns>
    private Task FollowDraftAsync() => _read is { } read && Go.IsEnabled ? read(PathInput.Text!.Trim()) : Task.CompletedTask;
}
