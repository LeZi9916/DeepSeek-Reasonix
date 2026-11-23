using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.Composer;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Host;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Sections in reference-frontend navigation order.</summary>
    private static readonly (string Id, string Title)[] s_sections =
    {
        ("common", "settings.common"), ("model", "common.models"), ("session", "settings.agent_behaviour"),
        ("tools", "settings.permissions_and_safety"), ("ext", "ext.extension"), ("display", "settings.conversation_display"),
        ("controls", "settings.controls_and_performance"), ("appearance", "settings.appearance"), ("storage", "settings.data"),
        ("network", "usage.connections"), ("account", "settings.account_and_about"),
    };

    /// <summary>The appearance the host returned or acknowledged.</summary>
    private JsonObject _look = new();

    /// <summary>Identifies the active settings read when the reader navigates quickly.</summary>
    private long _settingsRead;

    /// <summary>Gets or sets the device-local preset used when opening a new task.</summary>
    internal string? NewTaskPreset
    {
        get; set;
    }

    /// <summary>Gets or sets the persistence callback installed by the desktop lifetime.</summary>
    internal Action? SaveWindowPreferences
    {
        get; set;
    }

    /// <summary>Connects the settings sheet and the first-run screen to window navigation.</summary>
    private void InitializePreferences()
    {
        OpenSettings.Click += (_, _) => OpenPreferences();
        InitializeSidebar();
        Label(OpenProject, MessageKey.App.OpenProjectFolder);
        OpenProject.Click += async (_, _) =>
        {
            if (_workspaceRoot.Length > 0)
            {
                var refusal = await new AvaloniaHost(this).OpenWorkspaceAsync(_workspaceRoot).ConfigureAwait(true);
                if (refusal is not null)
                {
                    ErrorText.Text = refusal.Error;
                    ErrorBar.IsVisible = true;
                }
            }
        };
        CloseSettings.Click += (_, _) => ReturnToConversation();
        InitializeSettingsSearch();
        SettingsNav.SelectionChanged += async (_, _) =>
        {
            if (!_selectingSetting && SettingsNav.SelectedItem is SectionEntry entry)
            {
                await ShowSectionAsync(entry.Id).ConfigureAwait(true);
            }
        };
        OpenSettings.Content = MessageCatalog.Get(MessageKey.Common.Settings, Language);
        InitializeSetup();
    }

    /// <summary>Adopts machine preferences without holding up the session stream.</summary>
    /// <returns>The initial appearance and onboarding reads.</returns>
    private async Task LoadStartupPreferencesAsync()
    {
        var engine = _launchOwner ?? _engine;
        if (engine is null)
        {
            return;
        }

        try
        {
            var setup = await engine.Pages.ProviderSetupAsync(_closing.Token).ConfigureAwait(true);
            if (setup is { Required: true } && !_closing.IsCancellationRequested)
            {
                ShowSetup(setup);
            }

            var look = await engine.Pages.AppearanceAsync(_closing.Token).ConfigureAwait(true);
            var fold = await engine.Pages.FlagAsync(StudioSetting.FoldWork, _closing.Token).ConfigureAwait(true);
            ApplyAppearance(look, fold);
            await LoadPaletteAsync(engine.Pages).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            ShowRefusal(error);
        }
    }

    /// <summary>Adopts the canonical language and zoom in the already-mounted window.</summary>
    /// <param name="look">Appearance returned by the host.</param>
    /// <param name="fold">Whether to collapse intermediate work.</param>
    private void ApplyAppearance(JsonObject look, bool fold)
    {
        _look = look;
        var zoom = (double?)look["zoom"] ?? 1;
        zoom = zoom > 0 ? zoom : 1;
        ScaledWindow.LayoutTransform = zoom == 1 ? null : new ScaleTransform(zoom, zoom);
        var language = LanguageResolver.Resolve((string?)look["language"], new[] { CultureInfo.CurrentUICulture.Name });
        SetWindowTypography(look, language);
        Conversation.ApplyPreferences(language, fold);
        Projects.Language = language;
        Projects.RefreshLabels();
        LabelChrome();
        Label(Attach, MessageKey.Composer.AddAnAttachment);
        Label(ModelButton, MessageKey.Composer.ChooseModel);
        LabelComposeTools();
        OpenSettings.Content = MessageCatalog.Get(MessageKey.Common.Settings, Language);
        BackLabel.Text = MessageCatalog.Get(MessageKey.Settings.BackToWorkspace, Language);
        Avalonia.Automation.AutomationProperties.SetName(CloseSettings, BackLabel.Text);
        SettingsTitle.Text = MessageCatalog.Get(MessageKey.Common.Settings, Language);
        SettingsSearch.PlaceholderText = MessageCatalog.Get(MessageKey.Settings.SearchSettings, Language);
        WorkbenchView.Header = MessageCatalog.Get(MessageKey.Common.Workbench, Language);
        SaveFileLabel.Text = MessageCatalog.Get(MessageKey.Common.Save, Language);
        Label(FindButton, MessageKey.Chat.Search);
        FindText.PlaceholderText = MessageCatalog.Get(MessageKey.Chat.Search, Language);
        ExplorerHeading.Text = MessageCatalog.Get(MessageKey.Workbench.Explorer, Language);
        BrowserAction.Header = MessageCatalog.Get(MessageKey.Workbench.Browser, Language);
        TerminalAction.Header = MessageCatalog.Get(MessageKey.Workbench.Terminal, Language);
        ShowComposerState(Conversation.State);
        LabelSetup();
    }

    /// <summary>Saves a device-local new-task choice without changing any existing conversation.</summary>
    /// <param name="id">Builtin or personal preset.</param>
    private void SetNewTaskPreset(string? id)
    {
        var old = NewTaskPreset;
        NewTaskPreset = id;
        try
        {
            SaveWindowPreferences?.Invoke();
        }
        catch
        {
            NewTaskPreset = old;
            throw;
        }
    }

    /// <summary>Opens the settings sheet and focuses its search box.</summary>
    private void OpenPreferences()
    {
        QuickLinksPopup.IsOpen = false;
        DisarmStop();
        SetupPanel.IsVisible = false;
        LayoutRoot.IsVisible = false;
        SettingsPanel.IsVisible = true;
        EnterView(SettingsPanel);
        BackLabel.Text = MessageCatalog.Get(MessageKey.Settings.BackToWorkspace, Language);
        Avalonia.Automation.AutomationProperties.SetName(CloseSettings, BackLabel.Text);
        SettingsTitle.Text = MessageCatalog.Get(MessageKey.Common.Settings, Language);
        SettingsSearch.PlaceholderText = MessageCatalog.Get(MessageKey.Settings.SearchSettings, Language);
        SettingsSearch.Text = string.Empty;
        FilterPreferences();
        SettingsPanel.Focus();
    }

    /// <summary>Restores the existing session and draft after closing a full-window sheet.</summary>
    private async void ReturnToConversation()
    {
        if (SettingsPanel.IsVisible && !await MayLeaveSettingsAsync().ConfigureAwait(true))
        {
            return;
        }

        _unsavedSettings = null;
        _unsavedSettingsSummary = null;
        StopAccountSettings();
        SearchResultsPanel.IsVisible = false;
        _settingsRead++;
        SetupPanel.IsVisible = false;
        SettingsPanel.IsVisible = false;
        LayoutRoot.IsVisible = true;
        ServiceKey.Text = string.Empty;
        Composer.Focus();
    }

    /// <summary>Filters navigation by localized section labels.</summary>
    private void FilterPreferences()
    {
        var previous = (SettingsNav.SelectedItem as SectionEntry)?.Id ?? "common";
        var sections = s_sections.Select(s => new SectionEntry(
            s.Id,
            MessageCatalog.Get(s.Title, Language),
            this.FindResource("Glyph." + s.Id) as Geometry,
            Format.Count(BlockCount(s.Id), Language))).ToArray();
        _selectingSetting = true;
        try
        {
            SettingsNav.ItemsSource = sections;
        }
        finally
        {
            _selectingSetting = false;
        }

        SettingsNav.SelectedItem = sections.First(s => s.Id == previous);
        ShowSettingsHits();
    }

    /// <summary>Builds settings controls from their canonical owners.</summary>
    /// <param name="section">Reference section id.</param>
    /// <returns>The current section's data reads.</returns>
    private async Task ShowSectionAsync(string section)
    {
        if (!await MayLeaveSettingsAsync().ConfigureAwait(true))
        {
            SelectSettingsNav(_shownSettings.Split(':')[0]);
            return;
        }

        _unsavedSettings = null;
        _unsavedSettingsSummary = null;
        StopAccountSettings();
        _shownSettings = section;
        var ticket = ++_settingsRead;
        var parent = section.Split(':')[0];
        SettingsHeadingBlock.IsVisible = true;
        SettingsHeading.Text = MessageCatalog.Get(parent == "usage" ? "app.usage" : s_sections.First(s => s.Id == parent).Title, Language);
        SettingsDescription.Text = SectionDescription(parent);
        SettingsDescription.IsVisible = SettingsDescription.Text.Length > 0;
        SettingsUsual.Text = MessageCatalog.Get("settings.page_usual", Language);
        SettingsError.Text = string.Empty;
        SettingsIndex.IsVisible = false;
        SettingsBody.Classes.Remove("settings-card-footer");
        SettingsLinks.Children.Clear();
        var column = new StackPanel { Spacing = 16 };
        SettingsContent.Content = column;
        SettingsScroller.Offset = default;
        var api = _engine?.Pages;
        try
        {
            switch (section)
            {
                case "common":
                    if (api is not null)
                    {
                        var start = Card(column, "common.start");
                        await StartSettingsAsync(start, api).ConfigureAwait(true);
                    }

                    var input = Card(column, "settings.input_and_reminders");
                    LanguageChoice(input);
                    ShortcutChoice(input);
                    if (api is not null)
                    {
                        await NotificationsAsync(input, api).ConfigureAwait(true);
                    }

                    ThemeChoice(input);
                    if (api is not null && await api.ChangedSettingsAsync(_closing.Token).ConfigureAwait(true) is { } changed)
                    {
                        var view = new Settings.ChangedSettings();
                        view.Show(changed.Select(key => key!.GetValue<string>()).ToArray(), Language);
                        Card(column, "settings.changed_settings").Children.Add(view);
                    }

                    break;
                case "appearance":
                    var theme = Card(column, "settings.theme_card");
                    ThemeChoice(theme);
                    if (api is not null)
                    {
                        await ShowPalettesAsync(theme, api).ConfigureAwait(true);
                    }

                    EffectsChoice(theme);
                    if (api is not null)
                    {
                        var look = await api.AppearanceAsync(_closing.Token).ConfigureAwait(true);
                        var type = new Settings.TypographyForm { Tag = "look.typeface" };
                        Card(column, "look.typeface").Children.Add(type);
                        type.Open(look, Language, api, accepted => ApplyAppearance(accepted, Conversation.FoldsTurnWork), DescribeError, _closing.Token);
                        ZoomChoice(Card(column, "common.window"), look);
                    }

                    break;
                case "controls":
                    ShowOpsSettings(column);
                    break;
                case "model" when api is not null:
                    var services = Card(column, "models.services");
                    if (column.Children.LastOrDefault() is Border servicesCard)
                    {
                        servicesCard.Tag = "common.model_services";
                    }

                    var providers = await api.ProvidersAsync(_closing.Token).ConfigureAwait(true);
                    var serviceView = new Settings.ModelServices { Tag = "models.services.view", Margin = new Thickness(0, 8, 0, 0) };
                    serviceView.OrderChanged += order =>
                    {
                        var unresolved = _workspacePreferences.ProviderOrder.Where(name => !order.Contains(name, StringComparer.Ordinal));
                        _workspacePreferences = _workspacePreferences with
                        {
                            ProviderOrder = order.Concat(unresolved).ToArray(),
                        };
                        if (_connectOnOpen)
                        {
                            _workspacePreferences.Save();
                        }
                    };
                    serviceView.DeleteRequested += async name =>
                    {
                        var title = MessageCatalog.Get("common.delete_name", Language, ("name", name));
                        if (await AskWorkspaceAsync(title, name).ConfigureAwait(true) is null)
                        {
                            return;
                        }

                        await api.RemoveProviderAsync(name, _closing.Token).ConfigureAwait(true);
                        await ShowSectionAsync("model").ConfigureAwait(true);
                    };
                    serviceView.Show(providers, Language, api, _closing.Token, _workspacePreferences.ProviderOrder, MayLeaveSettingsAsync, DescribeError);
                    services.Children.Add(new Settings.SettingsGroup("common.model_services", null, serviceView, Language, scope: "settings.rebuilds_the_runtime"));
                    _unsavedSettings = () => serviceView.HasChanges;
                    await FlagControlAsync(services, StudioSetting.ProviderHealth, "models.automatically_check_connections_and_balances").ConfigureAwait(true);
                    var uses = Card(column, "models.uses_and_context");
                    var usage = new Settings.ModelUsageView();
                    var maintenance = new Settings.ContextMaintenanceView();
                    uses.Children.Add(new Settings.SettingsGroup(
                        "settings.models_by_use",
                        "models.which_model_for_each_use",
                        usage,
                        Language,
                        scope: "settings.rebuilds_the_runtime"));
                    uses.Children.Add(new Settings.SettingsGroup(
                        "settings.context_maintenance",
                        null,
                        maintenance,
                        Language,
                        scope: "settings.rebuilds_the_runtime"));
                    await Task.WhenAll(
                        usage.OpenAsync(
                            api,
                            serviceView,
                            () => _engine?.StatusAsync(_closing.Token) ?? Task.FromResult<SessionStatus?>(null),
                            async reference =>
                            {
                                await api.SetModelAsync(reference, _closing.Token).ConfigureAwait(true);
                                var preferenceOwner = _launchOwner?.Pages ?? api;
                                await preferenceOwner.SetDefaultModelAsync(reference, _closing.Token).ConfigureAwait(true);
                            },
                            Language,
                            DescribeError,
                            _closing.Token),
                        maintenance.OpenAsync(api, Language, DescribeError, _closing.Token)).ConfigureAwait(true);
                    break;
                case "session" when api is not null:
                    var modes = Card(column, "settings.persona_and_presets");
                    AddPersonaEntry(modes);
                    var presets = new Settings.PresetWorkspace { Tag = "persona.agent_presets" };
                    modes.Children.Add(presets);
                    var definitions = await api.AgentPresetsAsync(_closing.Token).ConfigureAwait(true);
                    var activePreset = await api.CurrentCompositionAsync(_closing.Token).ConfigureAwait(true);
                    presets.Open(
                        api,
                        Language,
                        definitions,
                        activePreset,
                        () => NewTaskPreset,
                        SetNewTaskPreset,
                        MayLeaveSettingsAsync,
                        () => ShowSectionAsync("session:persona"),
                        DescribeError,
                        _closing.Token);
                    presets.ConnectModePicker(_workspacePreferences.ShowModePicker, show =>
                    {
                        _workspacePreferences = _workspacePreferences with
                        {
                            ShowModePicker = show,
                        };
                        if (_connectOnOpen)
                        {
                            _workspacePreferences.Save();
                        }

                        PlanMode.IsVisible = show;
                    });
                    if (ticket == _settingsRead)
                    {
                        _unsavedSettings = () => presets.HasChanges;
                        _unsavedSettingsSummary = presets.Changes;
                    }

                    await Task.WhenAll(
                        AgentPlanSettingsAsync(column, api),
                        AgentCollaborationAsync(column, api),
                        AgentAutomationAsync(column, api)).ConfigureAwait(true);
                    break;
                case "session:persona" when api is not null:
                    var persona = Card(column, "settings.persona_and_presets");
                    AddPersonaEntry(persona);
                    var personaView = new Settings.SystemPersonaView { Tag = "persona.agent_presets" };
                    persona.Children.Add(personaView);
                    var activePersona = await api.PersonaAsync(_closing.Token).ConfigureAwait(true);
                    personaView.Open(api, activePersona, Language, () => ShowSectionAsync("session"), DescribeError, _closing.Token);
                    if (ticket == _settingsRead)
                    {
                        _unsavedSettings = () => personaView.HasChanges;
                    }

                    await Task.WhenAll(
                        AgentPlanSettingsAsync(column, api),
                        AgentCollaborationAsync(column, api),
                        AgentAutomationAsync(column, api)).ConfigureAwait(true);
                    break;
                case "tools" when api is not null:
                    await SafetySettingsAsync(column, api).ConfigureAwait(true);
                    break;
                case "display":
                    var process = Card(column, "settings.process_card");
                    await FlagControlAsync(process, StudioSetting.FoldWork, "settings.fold_the_work_in_between").ConfigureAwait(true);
                    var folding = new Settings.FoldingSettings { Tag = "chat.transcript_folding" };
                    process.Children.Add(folding);
                    folding.Open(Language, _workspacePreferences.Folding ?? new(), next =>
                    {
                        _workspacePreferences = _workspacePreferences with
                        {
                            Folding = next,
                        };
                        if (_connectOnOpen)
                        {
                            _workspacePreferences.Save();
                        }

                        Conversation.SetTranscriptFolding(next);
                    });
                    AddPlanProgressSetting(process);
                    var previews = Card(column, "settings.files_and_previews_card");
                    await FlagControlAsync(previews, StudioSetting.ChangedFiles, "settings.file_changes").ConfigureAwait(true);
                    await FlagControlAsync(previews, StudioSetting.PresentFiles, "settings.ai_file_delivery_cards").ConfigureAwait(true);
                    AddOfficePreviewSetting(previews);
                    AddNewAnswerMarkerSetting(Card(column, "settings.notices_card"));
                    break;
                case "usage" when api is not null:
                    await AddUsageAsync(Card(column, "app.usage"), api).ConfigureAwait(true);
                    break;
                case "storage":
                    if (api is not null)
                    {
                        await BuildDataPageAsync(column, api).ConfigureAwait(true);
                    }

                    break;
                case "ext" or "ext:market" when api is not null:
                    await PackageSettingsAsync(column, api, section == "ext:market").ConfigureAwait(true);
                    break;
                case "network" when api is not null:
                    var proxy = new Settings.NetworkSettings();
                    Card(column, "common.network").Children.Add(proxy);
                    await proxy.OpenAsync(api, Language, DescribeError, _closing.Token).ConfigureAwait(true);
                    if (TopLevel.GetTopLevel(proxy) == this)
                    {
                        _unsavedSettings = () => proxy.HasChanges;
                    }

                    var remotes = await api.RemoteHostsAsync(_closing.Token).ConfigureAwait(true);
                    if (remotes is not null)
                    {
                        var aliases = await api.RemoteCandidatesAsync(_closing.Token).ConfigureAwait(true);
                        var distros = await api.RemoteDistrosAsync(_closing.Token).ConfigureAwait(true);
                        var book = new Settings.RemoteBook { Tag = "settings.remote" };
                        Card(column, "settings.other_machines").Children.Add(book);
                        book.Open(
                            api,
                            remotes,
                            aliases,
                            distros,
                            Language,
                            DescribeError,
                            (IsRemoteHidden, ToggleRemoteRailAsync),
                            (host, start) => PickRemoteFolderAsync(api, host, start),
                            _closing.Token);
                        if (TopLevel.GetTopLevel(book) == this)
                        {
                            var proxyDirty = _unsavedSettings;
                            _unsavedSettings = () => book.HasChanges || proxyDirty?.Invoke() == true;
                        }
                    }

                    break;
                case "account" when api is not null:
                    await ShowAccountSettingsAsync(column, api).ConfigureAwait(true);
                    break;
            }

            if (ticket == _settingsRead)
            {
                SettingsBody.Classes.Set("settings-card-footer", column.Children.LastOrDefault() is Border last && last.Classes.Contains("settings-card"));
                IndexSettings(column);
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (ticket == _settingsRead)
            {
                SettingsError.Text = DescribeError(error);
            }
        }
    }

    /// <summary>Shows the system persona entry above the presets and the persona editor.</summary>
    /// <param name="content">The settings card.</param>
    private void AddPersonaEntry(StackPanel content)
    {
        var edit = new Button
        {
            Content = MessageCatalog.Get("settings.edit_persona", Language),
            Classes = { "preset-act" },
        };
        AddRow(content, MessageKey.Persona.SystemPersona, edit, MessageCatalog.Get("settings.persona_hint", Language));
        AutomationProperties.SetAutomationId(edit, "action:settings.edit-persona");
        AutomationProperties.SetName(edit, (string)edit.Content);
        edit.Click += async (_, _) => await ShowSectionAsync("session:persona").ConfigureAwait(true);
    }

    /// <summary>Adds the window-local effects preference.</summary>
    /// <param name="content">The settings column.</param>
    private void EffectsChoice(StackPanel content)
    {
        var choices = new[]
        {
            new SectionEntry("full", MessageCatalog.Get(MessageKey.Look.Full, Language)),
            new SectionEntry("reduced", MessageCatalog.Get(MessageKey.Common.Off, Language)),
        };
        Choice(content, MessageKey.Look.VisualEffects, choices, ReducedEffects ? "reduced" : "full", id =>
        {
            ReducedEffects = id == "reduced";
            ApplyMotion();
            return Task.CompletedTask;
        });
    }

    /// <summary>Adds the immediate window-theme preference.</summary>
    /// <param name="content">The column.</param>
    private void ThemeChoice(StackPanel content)
    {
        var choices = new[]
        {
            new SectionEntry("Default", MessageCatalog.Get(MessageKey.Look.FollowTheSystem, Language)),
            new SectionEntry("Light", MessageCatalog.Get(MessageKey.Look.Light, Language)),
            new SectionEntry("Dark", MessageCatalog.Get(MessageKey.Look.Dark, Language)),
        };
        Choice(content, MessageKey.Settings.LightAndDark, choices, RequestedThemeVariant?.Key as string ?? "Default", id =>
        {
            RequestedThemeVariant = id == "Dark" ? ThemeVariant.Dark : id == "Light" ? ThemeVariant.Light : ThemeVariant.Default;
            return Task.CompletedTask;
        });
    }

    /// <summary>Adds the kernel-owned language preference.</summary>
    /// <param name="content">The column.</param>
    private void LanguageChoice(StackPanel content)
    {
        var choices = new[]
        {
            new SectionEntry(string.Empty, MessageCatalog.Get(MessageKey.Look.FollowTheSystem, Language)),
            new SectionEntry("zh", "中文"),
            new SectionEntry("en", "English"),
        };
        Choice(content, MessageKey.Common.Language, choices, (string?)_look["language"] ?? string.Empty, async id =>
        {
            if (_engine is not null)
            {
                var look = await _engine.Pages.SetAppearanceAsync(id, null, _closing.Token).ConfigureAwait(true);
                ApplyAppearance(look, Conversation.FoldsTurnWork);
                FilterPreferences();
            }
        });
    }

    /// <summary>Adds the desktop send chord preference.</summary>
    /// <param name="content">The column.</param>
    private void ShortcutChoice(StackPanel content)
    {
        var choices = new[] { new SectionEntry("enter", "Enter"), new SectionEntry("modifier", OperatingSystem.IsMacOS() ? "⌘ Enter" : "Ctrl+Enter") };
        Choice(content, MessageKey.Composer.SendShortcut, choices, _shortcut == SendShortcut.Enter ? "enter" : "modifier", id =>
        {
            _shortcut = id == "enter" ? SendShortcut.Enter : SendShortcut.ModifierEnter;
            SendShortcutStore.Save(_shortcut);
            ShowComposerState(Conversation.State);
            return Task.CompletedTask;
        });
    }

    /// <summary>Offers zoom levels within the range actually announced by the kernel.</summary>
    /// <param name="content">The column.</param>
    /// <param name="look">The current appearance.</param>
    private void ZoomChoice(StackPanel content, JsonObject look)
    {
        if (look["zoomRange"] is not JsonObject range || _engine is null)
        {
            return;
        }

        var zoom = new NumericUpDown
        {
            Minimum = (decimal?)range["min"] ?? 1,
            Maximum = (decimal?)range["max"] ?? 1,
            Increment = 0.05m,
            Value = (decimal?)look["zoom"] is > 0 and var value ? value : 1,
        };
        AddRow(content, MessageKey.Look.InterfaceSize, zoom);
        AddAction(content, MessageKey.Common.Save, async () =>
        {
            var applied = await _engine.Pages.SetAppearanceAsync(null, (double?)zoom.Value, _closing.Token).ConfigureAwait(true);
            ApplyAppearance(applied, Conversation.FoldsTurnWork);
        });
    }

    /// <summary>Shows a switch and waits for the canonical value before treating a write as applied.</summary>
    /// <param name="content">The column owning this control.</param>
    /// <param name="setting">The endpoint and field to drive.</param>
    /// <param name="key">The label key.</param>
    /// <returns>The setting read.</returns>
    private async Task FlagControlAsync(StackPanel content, StudioSetting setting, string key)
    {
        if (_engine is null)
        {
            return;
        }

        var api = _engine.Pages;
        var value = await api.FlagAsync(setting, _closing.Token).ConfigureAwait(true);
        var toggle = new ToggleSwitch { IsChecked = value, OnContent = string.Empty, OffContent = string.Empty, MinWidth = 0 };
        toggle.Click += async (_, _) =>
        {
            toggle.IsEnabled = false;
            try
            {
                value = await api.SetFlagAsync(setting, toggle.IsChecked == true, _closing.Token).ConfigureAwait(true);
                if (setting == StudioSetting.FoldWork)
                {
                    Conversation.ApplyPreferences(Language, value);
                }
            }
            catch (Exception error)
            {
                SettingsError.Text = DescribeError(error);
            }
            finally
            {
                toggle.IsChecked = value;
                toggle.IsEnabled = true;
            }
        };
        AddRow(content, key, toggle, applyKey: setting == StudioSetting.PresentFiles ? "settings.applies_to_new_sessions" : null);
    }

    /// <summary>Adds an action that remains retryable after a refusal.</summary>
    /// <param name="content">The settings column.</param>
    /// <param name="key">The action label.</param>
    /// <param name="run">The action to execute.</param>
    private void AddAction(StackPanel content, string key, Func<Task> run)
    {
        var button = new Button { Content = MessageCatalog.Get(key, Language), Tag = key, HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                await run().ConfigureAwait(true);
            }
            catch (Exception error)
            {
                SettingsError.Text = DescribeError(error);
            }
            finally
            {
                button.IsEnabled = true;
            }
        };
        content.Children.Add(button);
    }

    /// <summary>Adds a choice with rollback when its owner rejects the change.</summary>
    /// <param name="content">The settings column.</param>
    /// <param name="key">Label.</param>
    /// <param name="choices">Permitted values.</param>
    /// <param name="current">Current value.</param>
    /// <param name="save">Applies one selected value.</param>
    private void Choice(StackPanel content, string key, SectionEntry[] choices, string current, Func<string, Task> save)
    {
        var action = key switch
        {
            "composer.send_shortcut" => "composer.send-shortcut",
            "settings.light_and_dark" => "appearance.scheme",
            "settings.messages_sent_while_running" => "composer.running-message-intent",
            "look.visual_effects" => "appearance.effects",
            "about.update_mode" => "update.mode",
            _ => null,
        };
        if (action is not null)
        {
            SegmentChoice(content, key, action, choices, current, save);
            return;
        }

        var select = new ComboBox { Width = 140, Height = 30, ItemsSource = choices, SelectedItem = choices.FirstOrDefault(c => c.Id == current) };
        select.SelectionChanged += async (_, _) =>
        {
            if (!select.IsEnabled || select.SelectedItem is not SectionEntry entry || entry.Id == current)
            {
                return;
            }

            select.IsEnabled = false;
            try
            {
                await save(entry.Id).ConfigureAwait(true);
                current = entry.Id;
            }
            catch (Exception error)
            {
                SettingsError.Text = DescribeError(error);
                select.SelectedItem = choices.FirstOrDefault(c => c.Id == current);
            }
            finally
            {
                select.IsEnabled = true;
            }
        };
        AddRow(content, key, select);
    }

    /// <summary>A displayed label paired with its stable selection id.</summary>
    /// <param name="Id">Stored identity.</param>
    /// <param name="Title">Localized label.</param>
    /// <param name="Glyph">Optional navigation icon.</param>
    /// <param name="CountText">Number of settings exposed by this native section.</param>
    private sealed record SectionEntry(string Id, string Title, Geometry? Glyph = null, string? CountText = null)
    {
        /// <inheritdoc/>
        public override string ToString() => Title;
    }
}
