using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>The agent preset catalog and its version-aware definition editor.</summary>
public sealed partial class PresetWorkspace : SettingsFormView
{
    /// <summary>The original definition when the editor opened.</summary>
    private JsonObject? _original;

    /// <summary>An independent copy of the definition currently being edited.</summary>
    private JsonObject? _draft;

    /// <summary>Initializes a new instance of the <see cref="PresetWorkspace"/> class.</summary>
    public PresetWorkspace() => InitializeComponent();

    /// <summary>Gets a value indicating whether closing the editor would lose changes.</summary>
    internal bool HasChanges => Editor.IsVisible && (!Modules.IsValid || !JsonNode.DeepEquals(_original, _draft));

    /// <summary>Connects the catalog and the editor to this workspace.</summary>
    /// <param name="api">The active kernel.</param>
    /// <param name="language">The shell language.</param>
    /// <param name="catalog">Initial definition versions and module choices.</param>
    /// <param name="active">The composition bound to the current session.</param>
    /// <param name="preferred">Reads this device's new-task default.</param>
    /// <param name="setDefault">Saves the selected new-task default.</param>
    /// <param name="leave">The shell's unsaved-edit prompt.</param>
    /// <param name="describe">Localizes a refusal.</param>
    /// <param name="token">Window lifetime.</param>
    internal void Open(StudioPageClient api, InterfaceLanguage language, JsonObject catalog, JsonObject? active, Func<string?> preferred, Action<string?> setDefault, Func<Task<bool>> leave, Func<Exception, string> describe, CancellationToken token)
    {
        Translate(language);
        ActivePreset.IsVisible = active is not null;
        if (active is not null)
        {
            var name = (string?)active["name"] ?? string.Empty;
            var revision = (string?)active["revision"] ?? string.Empty;
            ActivePreset.Text = string.Join(" · ", Text("common.this_session"), name, revision[..Math.Min(12, revision.Length)], Text((bool?)active["started"] == true ? "persona.locked" : "persona.switch_before_the_first_message"));
        }

        var versions = (catalog["presets"] ??= new JsonArray()).AsArray();
        var scopes = new[] { new Option("builtin", Text("common.built_in")), new Option("user", Text("persona.personal")), new Option("project", Text("persona.shared_with_project")) };
        Scope.ItemsSource = scopes;
        JsonObject? version = null;
        Button? opener = null;
        var populating = false;
        var busy = false;
        var immutable = false;
        string? exported = null;
        Import.Open(api, language, PreviewImported, error => Message(describe(error), true), token);
        void PreviewImported(JsonObject imported)
        {
            Start(imported["definition"]!.AsObject(), null, true, Create);
            var diagnostics = (imported["diagnostics"]?.AsArray() ?? []).Select(d => (string?)d?["detail"]).Where(value => !string.IsNullOrEmpty(value));
            var summary = string.Join(Environment.NewLine, diagnostics);
            if (summary.Length > 0)
            {
                Message(summary, false);
            }
        }

        Refresh.Click += async (_, _) => await RunExtraAsync(async () =>
        {
            catalog = await api.AgentPresetsAsync(token).ConfigureAwait(true);
            versions = (catalog["presets"] ??= new JsonArray()).AsArray();
            List(null);
        }).ConfigureAwait(true);
        Export.Click += async (_, _) => await RunExtraAsync(async () =>
        {
            var definition = await api.ExportPresetAsync((string)version!["definition"]!["id"]!, token).ConfigureAwait(true);
            exported = definition.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            Download.IsVisible = true;
        }).ConfigureAwait(true);
        Download.Click += async (_, _) => await RunExtraAsync(async () =>
        {
            if (exported is not null && TopLevel.GetTopLevel(this) is Window window)
            {
                await new Host.AvaloniaHost(window).SaveTextAsync("agent-preset.json", exported).ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
        Remove.Click += (_, _) => ConfirmRemove.IsVisible = Keep.IsVisible = true;
        Keep.Click += (_, _) => ConfirmRemove.IsVisible = Keep.IsVisible = false;
        ConfirmRemove.Click += async (_, _) => await RunExtraAsync(async () =>
        {
            var id = (string)version!["definition"]!["id"]!;
            await api.DeletePresetAsync(id, (string)version["revision"]!, token).ConfigureAwait(true);
            versions.Remove(version);
            if (preferred() == id)
            {
                setDefault(null);
            }

            CloseEditor();
            List(null);
            Message(Text("persona.preset_deleted_existing_conversations_keep"), false);
        }).ConfigureAwait(true);
        Personal.Click += async (_, _) => await ExplicitDefaultAsync(false).ConfigureAwait(true);
        Workspace.Click += async (_, _) => await ExplicitDefaultAsync(true).ConfigureAwait(true);
        Create.Click += (_, _) =>
        {
            if (versions.FirstOrDefault(v => (string?)v?["definition"]?["id"] == "builtin:general")?["definition"] is JsonObject basis)
            {
                Start(basis, null, false, Create);
            }
        };
        Duplicate.Click += (_, _) => Start(_draft!, null, true, opener);
        Back.Click += async (_, _) => await BackAsync().ConfigureAwait(true);
        Cancel.Click += async (_, _) => await BackAsync().ConfigureAwait(true);
        KeyDown += async (_, e) =>
        {
            if (Editor.IsVisible && e.Key == Key.Escape)
            {
                e.Handled = true;
                await BackAsync().ConfigureAwait(true);
            }
        };
        foreach (var input in new[] { PresetName, Purpose, Prefix, Suffix, Identity })
        {
            input.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty)
                {
                    Update();
                }
            };
        }

        Scope.SelectionChanged += (_, _) => Update();
        OmitRuntime.IsCheckedChanged += (_, _) => Update();
        Save.Click += async (_, _) =>
        {
            if (busy || !Save.IsEnabled || _draft is null)
            {
                return;
            }

            busy = true;
            Editor.IsEnabled = false;
            try
            {
                var saved = await api.SavePresetAsync(_draft.DeepClone().AsObject(), (string?)version?["revision"] ?? string.Empty, token).ConfigureAwait(true);
                var id = (string)saved["definition"]!["id"]!;
                var old = versions.FirstOrDefault(v => (string?)v?["definition"]?["id"] == id);
                if (old is not null)
                {
                    versions.Remove(old);
                }

                versions.Add(saved);
                CloseEditor();
                List(id);
                Message(Text("persona.saved_new_conversations_use_the"), false);
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    Message(describe(error), true);
                    Duplicate.IsVisible = error is KernelRefusal { Code: "agent_composition.conflict" } && version is not null;
                }
            }
            finally
            {
                busy = false;
                Editor.IsEnabled = true;
            }
        };
        List(null);

        void List(string? focus)
        {
            Groups.Children.Clear();
            Create.IsEnabled = versions.Any(v => (string?)v?["definition"]?["id"] == "builtin:general");
            foreach (var scope in scopes)
            {
                var items = versions.OfType<JsonObject>().Where(v => ((string?)v["definition"]?["id"])?.StartsWith(scope.Id + ":", StringComparison.Ordinal) == true).ToArray();
                if (items.Length == 0)
                {
                    continue;
                }

                Groups.Children.Add(new TextBlock { Text = scope.Label, FontWeight = FontWeight.SemiBold });
                var tiles = new Avalonia.Controls.Primitives.UniformGrid { Columns = 1 };
                if (scope.Id == "builtin")
                {
                    tiles.SizeChanged += (_, e) => tiles.Columns = e.NewSize.Width >= 650 ? 2 : 1;
                }

                Groups.Children.Add(tiles);
                foreach (var v in items)
                {
                    var d = v["definition"]!.AsObject();
                    var id = (string)d["id"]!;
                    var copy = new StackPanel { Spacing = 5 };
                    var card = new Border { Padding = new Thickness(16), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 8), Child = copy };
                    card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("HairBrush"));
                    copy.Children.Add(new TextBlock { Text = Label(d, "name"), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
                    copy.Children.Add(Caption(Label(d, "description")));
                    var actions = new WrapPanel();
                    var view = new Button { Tag = id, Content = Text(scope.Id == "builtin" ? "persona.mode_details" : "common.edit"), Margin = new Thickness(0, 0, 4, 0) };
                    var duplicate = new Button { Tag = id, Content = Text("common.copy") };
                    AutomationProperties.SetAutomationId(view, "action:agent-preset.view");
                    AutomationProperties.SetAutomationId(duplicate, "action:agent-preset.copy");
                    view.Click += (_, _) => Start(d, v, false, view);
                    duplicate.Click += (_, _) => Start(d, null, true, duplicate);
                    if (scope.Id != "project")
                    {
                        var selected = id == (preferred() ?? "builtin:general");
                        var pick = new Button { Tag = id, Content = Text(selected ? "persona.new_task_default" : "common.make_default"), IsEnabled = !selected, Margin = new Thickness(0, 0, 4, 0) };
                        AutomationProperties.SetAutomationId(pick, "action:agent-preset.new-task-default");
                        pick.Click += (_, _) =>
                        {
                            try
                            {
                                setDefault(id);
                                List(id);
                            }
                            catch (Exception error)
                            {
                                Message(describe(error), true);
                            }
                        };
                        actions.Children.Add(pick);
                        card.Bind(Border.BorderBrushProperty, card.GetResourceObservable(selected ? "AccentBrush" : "HairBrush"));
                    }

                    actions.Children.Add(view);
                    actions.Children.Add(duplicate);
                    copy.Children.Add(actions);
                    tiles.Children.Add(card);
                    if (id == focus)
                    {
                        view.Focus();
                    }
                }
            }

            foreach (var issue in catalog["diagnostics"]?.AsArray().OfType<JsonObject>() ?? [])
            {
                Groups.Children.Add(Caption((string?)issue["detail"] ?? string.Empty));
            }
        }

        string Label(JsonObject d, string key) => ((string?)d["id"])?.StartsWith("builtin:", StringComparison.Ordinal) == true ? Text((string?)d[key] ?? string.Empty) : (string?)d[key] ?? string.Empty;

        void Start(JsonObject basis, JsonObject? existing, bool duplicate, Button? origin)
        {
            populating = true;
            version = existing;
            opener = origin;
            _draft = basis.DeepClone().AsObject();
            if (existing is null)
            {
                var slug = "custom";
                for (var i = 1; versions.Any(v => (string?)v?["definition"]?["id"] == "user:" + slug); i++)
                {
                    slug = "custom-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                _draft["id"] = "user:" + slug;
                _draft["name"] = duplicate ? Label(basis, "name") + " " + Text("persona.copy") : string.Empty;
                _draft["description"] = duplicate ? Label(basis, "description") : string.Empty;
            }

            _original = _draft.DeepClone().AsObject();
            exported = null;
            Download.IsVisible = ConfirmRemove.IsVisible = Keep.IsVisible = Extra.IsExpanded = false;
            Extra.IsVisible = existing is not null;
            var id = ((string)_draft["id"]!).Split(':', 2);
            immutable = id[0] == "builtin";
            Remove.IsVisible = !immutable;
            Personal.IsEnabled = id[0] != "project";
            PresetName.IsReadOnly = Purpose.IsReadOnly = Prefix.IsReadOnly = Suffix.IsReadOnly = immutable;
            OmitRuntime.IsEnabled = !immutable;
            Scope.IsEnabled = Identity.IsEnabled = existing is null;
            Scope.ItemsSource = existing is null ? scopes.Skip(1).ToArray() : scopes;
            Scope.SelectedItem = scopes.First(s => s.Id == id[0]);
            Identity.Text = id[1];
            PresetName.Text = Label(_draft, "name");
            Purpose.Text = Label(_draft, "description");
            Prefix.Text = (string?)_draft["persona"]?["prefix"] ?? string.Empty;
            Suffix.Text = (string?)_draft["persona"]?["suffix"] ?? string.Empty;
            OmitRuntime.IsChecked = (bool?)_draft["persona"]?["include_runtime_context"] != true;
            Modules.Open(_draft, catalog["modules"] as JsonObject, Language, immutable, Update);
            Heading.Text = Text(immutable ? "persona.preset_details" : existing is null ? "persona.new_preset" : "persona.edit_preset");
            SourceLabel.Text = existing is not null ? Label(basis, "name") : Text("persona.based_on_name", ("name", Label(basis, "name")));
            var revision = (string?)existing?["revision"] ?? string.Empty;
            Revision.IsVisible = revision.Length > 0;
            Revision.Text = Text("persona.preset_version") + " · " + revision[..Math.Min(12, revision.Length)];
            AutomationProperties.SetName(Back, Text("persona.back_to_presets"));
            Save.IsVisible = !immutable;
            Duplicate.IsVisible = immutable;
            Catalog.IsVisible = false;
            Editor.IsVisible = true;
            Message(string.Empty, false);
            populating = false;
            Update();
            this.BringIntoView();
            (immutable ? (Control)Back : PresetName).Focus();
        }

        void Update()
        {
            if (populating || immutable || _draft is null || !Editor.IsVisible)
            {
                return;
            }

            _draft["name"] = PresetName.Text ?? string.Empty;
            _draft["description"] = Purpose.Text ?? string.Empty;
            _draft["id"] = ((Option?)Scope.SelectedItem)?.Id + ":" + Identity.Text;
            var persona = (_draft["persona"] ??= new JsonObject()).AsObject();
            persona["prefix"] = Prefix.Text ?? string.Empty;
            persona["suffix"] = Suffix.Text ?? string.Empty;
            persona["include_runtime_context"] = OmitRuntime.IsChecked != true;
            Extra.IsVisible = version is not null && !HasChanges;
            Save.IsEnabled = !busy && Modules.IsValid && !string.IsNullOrWhiteSpace(PresetName.Text)
                && IsPresetId((string)_draft["id"]!) && (version is null || HasChanges);
        }

        async Task RunExtraAsync(Func<Task> action)
        {
            if (busy)
            {
                return;
            }

            busy = true;
            IsEnabled = false;
            try
            {
                await action().ConfigureAwait(true);
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    Message(describe(error), true);
                }
            }
            finally
            {
                busy = false;
                IsEnabled = true;
            }
        }

        Task ExplicitDefaultAsync(bool workspace) => RunExtraAsync(async () =>
        {
            await api.SetPresetDefaultAsync((string)version!["definition"]!["id"]!, workspace, token).ConfigureAwait(true);
            Message(Text(workspace ? "persona.set_as_workspace_preference" : "persona.set_as_personal_preference"), false);
        });

        async Task BackAsync()
        {
            if (!busy && await leave().ConfigureAwait(true))
            {
                CloseEditor();
                opener?.Focus();
            }
        }

        void CloseEditor()
        {
            Editor.IsVisible = false;
            Catalog.IsVisible = true;
            _original = _draft = null;
            Message(string.Empty, false);
        }
    }

    /// <summary>Validates the kernel's user and project preset identity grammar.</summary>
    /// <param name="id">Complete scoped id.</param>
    /// <returns>Whether it can be saved.</returns>
    private static bool IsPresetId(string id) => IdentityPattern().IsMatch(id);

    /// <summary>Matches the preset identity format.</summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(@"\A(user|project):[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityPattern();

    /// <summary>Displays feedback without hiding the edited fields.</summary>
    /// <param name="value">Visible feedback.</param>
    /// <param name="error">Whether the operation failed.</param>
    private void Message(string value, bool error)
    {
        Result.Text = value;
        Result.IsVisible = value.Length > 0;
        Result.Bind(TextBlock.ForegroundProperty, Result.GetResourceObservable(error ? "ErrBrush" : "MutedBrush"));
    }
}
