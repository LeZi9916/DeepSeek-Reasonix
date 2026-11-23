using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Shows the shared session message settings, live messages and released projects.</summary>
public sealed partial class PeerSettingsView : SettingsFormView
{
    /// <summary>Initializes a new instance of the <see cref="PeerSettingsView"/> class.</summary>
    public PeerSettingsView()
    {
        InitializeComponent();
        ProjectPanel.SizeChanged += (_, e) => ProjectPanel.Classes.Set("compact", e.NewSize.Width < 600);
    }

    /// <summary>Loads the preferences and begins watching the current session's peer state.</summary>
    /// <param name="api">The active session's settings endpoints.</param>
    /// <param name="language">The display language.</param>
    /// <param name="readProjects">Lists projects already added to this window.</param>
    /// <param name="describe">Localizes a request failure.</param>
    /// <param name="token">The host lifetime.</param>
    /// <returns>The initial settings, peer and project reads.</returns>
    internal async Task OpenAsync(
        StudioPageClient api,
        InterfaceLanguage language,
        Func<CancellationToken, Task<IReadOnlyList<TreeWorkspace>>> readProjects,
        Func<Exception, string> describe,
        CancellationToken token)
    {
        Translate(language);
        Projects.Tag = "remote.projects_allowed_through";
        Applies.Text = Text("session.this_machine") + " · " + Text("settings.applies_from_the_next_turn");
        AutomationProperties.SetName(Message, Text("remote.message"));
        JsonObject? settings = null;
        JsonObject? state = null;
        IReadOnlyList<TreeWorkspace> known = [];
        var writing = false;
        var applying = false;
        var peerLoadFailed = false;
        var query = 0;
        var peerVersion = string.Empty;
        var renderedHeld = string.Empty;
        var poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        poll.Tick += async (_, _) =>
        {
            if (IsEffectivelyVisible && !writing && !token.IsCancellationRequested)
            {
                await RefreshPeersAsync().ConfigureAwait(true);
            }
        };
        AttachedToVisualTree += (_, _) => poll.Start();
        DetachedFromVisualTree += (_, _) => poll.Stop();

        Enable.IsCheckedChanged += async (_, _) => await ChangeSwitchAsync(Enable, "enabled").ConfigureAwait(true);
        Dispatch.IsCheckedChanged += async (_, _) => await ChangeSwitchAsync(Dispatch, "dispatch").ConfigureAwait(true);
        Nested.IsCheckedChanged += async (_, _) => await ChangeSwitchAsync(Nested, "open_nested").ConfigureAwait(true);
        ReleaseAdd.Click += async (_, _) =>
        {
            if (ReleaseTarget.SelectedItem is Option option)
            {
                await WriteProjectsAsync(paths => paths.Append(option.Id)).ConfigureAwait(true);
            }
        };
        Send.Click += async (_, _) =>
        {
            if (Target.SelectedItem is not Option option || string.IsNullOrWhiteSpace(Message.Text))
            {
                return;
            }

            var text = Message.Text;
            await RunAsync(async () =>
            {
                var sent = await api.SendPeerMessageAsync(option.Id, text, token).ConfigureAwait(true);
                Receipt.Text = Text("remote.delivered_to_to_it_is", ("to", sent["to"] is JsonObject peer ? PeerLabel(peer) : option.Label));
                Receipt.IsVisible = true;
                Message.Text = string.Empty;
            }).ConfigureAwait(true);
        };
        Target.SelectionChanged += (_, _) => UpdateEnabled();
        ReleaseTarget.SelectionChanged += (_, _) => UpdateEnabled();
        Message.TextChanged += (_, _) => UpdateEnabled();
        await Task.WhenAll(ReadSettingsAsync(), RefreshPeersAsync(), ReadProjectsAsync()).ConfigureAwait(true);
        if (this.IsAttachedToVisualTree() && !token.IsCancellationRequested)
        {
            poll.Start();
        }

        async Task ReadSettingsAsync()
        {
            try
            {
                settings = await api.PeerSettingsAsync(token).ConfigureAwait(true);
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    SettingsFailure.Text = describe(error);
                    SettingsFailure.IsVisible = true;
                }
            }

            RenderSettings();
        }

        async Task ReadProjectsAsync()
        {
            try
            {
                known = (await readProjects(token).ConfigureAwait(true)).Where(project => project.Remembered && project.Root.Length > 0).ToArray();
            }
            catch (Exception)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                known = [];
            }

            RenderProjects();
        }

        async Task RefreshPeersAsync()
        {
            var request = ++query;
            try
            {
                var next = await api.PeerStateAsync(token).ConfigureAwait(true);
                if (request != query)
                {
                    return;
                }

                state = next;
                peerLoadFailed = next is null;
            }
            catch (Exception)
            {
                if (request != query || token.IsCancellationRequested)
                {
                    return;
                }

                peerLoadFailed = true;
            }

            RenderPeers();
        }

        void RenderSettings()
        {
            applying = true;
            Switches.IsVisible = settings is not null;
            Enable.IsChecked = (bool?)settings?["enabled"] == true;
            Dispatch.IsChecked = (bool?)settings?["dispatch"] == true;
            Nested.IsChecked = (bool?)settings?["open_nested"] == true;
            DispatchHint.Text = Text(Enable.IsChecked == true ? "remote.gives_the_model_a_tool" : "remote.turn_on_session_messages_first_2");
            Unavailable.IsVisible = settings is not null && (bool?)settings["available"] != true;
            Pending.IsVisible = (bool?)settings?["pending"] == true;
            SettingsPath.Inlines!.Clear();
            SettingsPath.Inlines.Add(new Run(Text("remote.these_switches_are_kept_in")));
            SettingsPath.Inlines.Add(new Run((string?)settings?["path"] ?? string.Empty)
            {
                [!Run.FontFamilyProperty] = StudioTypography.Resource("WorkbenchMonoFamily"),
                [!Run.ForegroundProperty] = StudioTypography.Resource("TextBrush"),
            });
            applying = false;
            RenderProjects();
        }

        void RenderProjects()
        {
            ReleaseList.Children.Clear();
            var released = Items(settings?["release_projects"]).ToArray();
            var remembered = released.Select(item => (string?)item["path"]).OfType<string>().ToHashSet(StringComparer.Ordinal);
            foreach (var item in released)
            {
                if ((string?)item["path"] is not { } path)
                {
                    continue;
                }

                var name = (string?)item["name"] ?? path;
                var revoke = ActionButton(Text("remote.revoke"), "peers.release-revoke", path);
                AutomationProperties.SetName(revoke, Text("remote.revoke_release_name", ("name", name)));
                revoke.Click += async (_, _) => await WriteProjectsAsync(paths => paths.Where(value => value != path)).ConfigureAwait(true);
                ReleaseList.Children.Add(Line(name, path, revoke));
            }

            var choices = known.Where(project => !remembered.Contains(project.Root))
                .Select(project => new Option(project.Root, project.Name + " — " + project.Root)).ToArray();
            var pick = (ReleaseTarget.SelectedItem as Option)?.Id;
            ReleaseTarget.ItemsSource = choices;
            ReleaseTarget.SelectedItem = choices.FirstOrDefault(choice => choice.Id == pick) ?? choices.FirstOrDefault();
            NoRelease.IsVisible = settings is not null && released.Length == 0;
            ReleaseForm.IsVisible = Enable.IsChecked == true && choices.Length > 0;
            ReleaseHint.IsVisible = settings is not null && !ReleaseForm.IsVisible;
            ReleaseHint.Text = Text(Enable.IsChecked == true ? "remote.there_is_no_project_to" : "remote.turn_on_session_messages_first");
            ReleaseUnavailable.IsVisible = Unavailable.IsVisible;
            ReleasePending.IsVisible = Pending.IsVisible;
            UpdateEnabled();
        }

        void RenderPeers()
        {
            Messages.IsVisible = (bool?)state?["enabled"] == true;
            PeerReadFailure.IsVisible = peerLoadFailed && !Messages.IsVisible;
            var peers = Items(state?["peers"]).ToArray();
            var version = state?["peers"]?.ToJsonString() ?? string.Empty;
            if (peerVersion != version)
            {
                peerVersion = version;
                var pick = (Target.SelectedItem as Option)?.Id;
                var choices = peers.Where(peer => peer["key"] is not null)
                    .Select(peer => new Option((string)peer["key"]!, PeerLabel(peer))).ToArray();
                Target.ItemsSource = choices;
                Target.SelectedItem = choices.FirstOrDefault(choice => choice.Id == pick) ?? choices.FirstOrDefault();
            }

            var heldVersion = state?["held"]?.ToJsonString() ?? string.Empty;
            if (renderedHeld != heldVersion)
            {
                renderedHeld = heldVersion;
                Held.Children.Clear();
                foreach (var item in Items(state?["held"]))
                {
                    if ((string?)item["id"] is not { } id)
                    {
                        continue;
                    }

                    var sender = Label((string?)item["fromProject"], (string?)item["fromTitle"], (string?)item["fromSession"] ?? string.Empty);
                    var deny = ActionButton(Text("common.refuse"), "peers.deny", id);
                    var approve = ActionButton(Text("remote.approve_and_give_it_to"), "peers.approve", id, true);
                    deny.Click += async (_, _) => await DecideAsync(id, false).ConfigureAwait(true);
                    approve.Click += async (_, _) => await DecideAsync(id, true).ConfigureAwait(true);
                    var buttons = new WrapPanel
                    {
                        ItemSpacing = 6,
                        LineSpacing = 4,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { deny, approve },
                    };
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
                    var heading = Caption(Text("remote.text_from_another_session_from", ("from", sender)));
                    heading.VerticalAlignment = VerticalAlignment.Center;
                    heading.Classes.Add("peer-note");
                    row.Children.Add(heading);
                    Grid.SetColumn(buttons, 1);
                    row.Children.Add(buttons);
                    var body = new TextBlock { Text = (string?)item["body"], TextWrapping = TextWrapping.Wrap };
                    var quote = new Border
                    {
                        Padding = new Thickness(8, 6),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(7),
                        [!Border.BorderBrushProperty] = StudioTypography.Resource("HairBrush"),
                        [!Border.BackgroundProperty] = StudioTypography.Resource("RaisedBrush"),
                        Child = new ScrollViewer { MaxHeight = 110, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = body },
                    };
                    Held.Children.Add(new StackPanel { Tag = "remote.text_from_another_session_from", Spacing = 4, Children = { row, quote } });
                }
            }

            NoPeer.IsVisible = peers.Length == 0;
            SendForm.IsVisible = peers.Length > 0;
            UpdateEnabled();
        }

        void UpdateEnabled()
        {
            var available = !writing && (bool?)settings?["available"] == true;
            Enable.IsEnabled = available;
            Dispatch.IsEnabled = available && Enable.IsChecked == true;
            Nested.IsEnabled = Dispatch.IsEnabled && Dispatch.IsChecked == true;
            ReleaseForm.IsEnabled = available;
            ReleaseList.IsEnabled = available;
            ReleaseAdd.IsEnabled = ReleaseTarget.SelectedItem is not null;
            Held.IsEnabled = !writing;
            Message.IsEnabled = Target.IsEnabled = !writing;
            Send.IsEnabled = !writing && Target.SelectedItem is not null && !string.IsNullOrWhiteSpace(Message.Text);
        }

        async Task ChangeSwitchAsync(ToggleSwitch toggle, string key)
        {
            if (!applying && !writing)
            {
                await RunAsync(async () =>
                {
                    settings = await api.SetPeerSettingsAsync(new JsonObject { [key] = toggle.IsChecked == true }, token).ConfigureAwait(true);
                    await RefreshPeersAsync().ConfigureAwait(true);
                }).ConfigureAwait(true);
            }
        }

        Task WriteProjectsAsync(Func<IEnumerable<string>, IEnumerable<string>> change) => RunAsync(async () =>
        {
            var latest = await api.PeerSettingsAsync(token).ConfigureAwait(true);
            if (latest is null)
            {
                await ReadSettingsAsync().ConfigureAwait(true);
                return;
            }

            var current = Items(latest["release_projects"]).Select(item => (string?)item["path"]).OfType<string>();
            settings = await api.SetPeerSettingsAsync(
                new JsonObject { ["release_projects"] = new JsonArray(change(current).Distinct(StringComparer.Ordinal).Select(path => (JsonNode?)JsonValue.Create(path)).ToArray()) },
                token).ConfigureAwait(true);
        });

        Task DecideAsync(string id, bool approve) => RunAsync(async () =>
        {
            await api.ResolvePeerMessageAsync(id, approve, token).ConfigureAwait(true);
            await RefreshPeersAsync().ConfigureAwait(true);
        });

        async Task RunAsync(Func<Task> execute)
        {
            if (writing)
            {
                return;
            }

            writing = true;
            WriteFailure.IsVisible = false;
            UpdateEnabled();
            try
            {
                await execute().ConfigureAwait(true);
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    WriteFailure.Text = describe(error);
                    WriteFailure.IsVisible = true;
                }
            }
            finally
            {
                writing = false;
                RenderSettings();
            }
        }
    }

    /// <summary>Reads the objects in a kernel list.</summary>
    /// <param name="node">The wire list.</param>
    /// <returns>Each object in order.</returns>
    private static IEnumerable<JsonObject> Items(JsonNode? node) => (node as JsonArray)?.OfType<JsonObject>() ?? [];

    /// <summary>Formats the name in the session chooser and delivery receipt.</summary>
    /// <param name="peer">The kernel's session entry.</param>
    /// <returns>The session label.</returns>
    private static string PeerLabel(JsonObject peer) => Label((string?)peer["project"], (string?)peer["title"], (string?)peer["key"] ?? string.Empty);

    /// <summary>Formats the same session identity for send targets and message authors.</summary>
    /// <param name="project">Optional project name.</param>
    /// <param name="title">Optional session title.</param>
    /// <param name="key">Stable fallback identity.</param>
    /// <returns>The visible label.</returns>
    private static string Label(string? project, string? title, string key) =>
        string.IsNullOrEmpty(title) ? key : string.IsNullOrEmpty(project) ? title : project + " / " + title;

    /// <summary>Creates a compact action with stable automation identity.</summary>
    /// <param name="label">The visible action name.</param>
    /// <param name="action">The action identifier.</param>
    /// <param name="id">The affected peer or project.</param>
    /// <param name="primary">Whether the action is emphasized.</param>
    /// <returns>The button.</returns>
    private static Button ActionButton(string label, string action, string id, bool primary = false)
    {
        var button = new Button
        {
            Content = label,
            Tag = id,
            Classes = { primary ? "preset-major" : "preset-act" },
            Height = 24,
            CornerRadius = new CornerRadius(999),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(button, "action:" + action);
        return button;
    }

    /// <summary>Displays a released project's title, path and revoke action.</summary>
    /// <param name="title">The display name.</param>
    /// <param name="path">The canonical path.</param>
    /// <param name="button">The action for this project.</param>
    /// <returns>The laid out row.</returns>
    private static Border Line(string title, string path, Button button)
    {
        var name = new TextBlock { Text = title, FontWeight = FontWeight.Medium, TextWrapping = TextWrapping.Wrap, LineHeight = 22.75 };
        var detail = new TextBlock
        {
            Text = path,
            LineHeight = 22.75,
            [!TextBlock.ForegroundProperty] = StudioTypography.Resource("MutedBrush"),
        };
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        detail.TextWrapping = TextWrapping.NoWrap;
        ToolTip.SetTip(detail, path);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        grid.Children.Add(new StackPanel { Children = { name, detail } });
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return new Border { Classes = { "peer-line" }, Child = grid, Padding = new Thickness(12, 6) };
    }
}
