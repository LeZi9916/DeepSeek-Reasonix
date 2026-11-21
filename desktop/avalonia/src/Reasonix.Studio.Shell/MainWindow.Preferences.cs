using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
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
        ("network", "usage.connections"), ("account", "settings.account_and_about"), ("advanced", "settings.advanced"),
    };

    /// <summary>The appearance the host returned or acknowledged.</summary>
    private JsonObject _look = new();

    /// <summary>Identifies the active settings read when the reader navigates quickly.</summary>
    private long _settingsRead;

    /// <summary>Connects the settings sheet and the first-run screen to window navigation.</summary>
    private void InitializePreferences()
    {
        OpenSettings.Click += (_, _) => OpenPreferences();
        OpenAccount.Click += (_, _) =>
        {
            OpenPreferences();
            SettingsNav.SelectedItem = SettingsNav.Items.OfType<SectionEntry>().First(s => s.Id == "account");
        };
        Label(OpenAccount, MessageKey.Settings.Account);
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
        if (_engine is null)
        {
            return;
        }

        try
        {
            var setup = await _engine.Pages.ProviderSetupAsync(_closing.Token).ConfigureAwait(true);
            if (setup is { Required: true } && !_closing.IsCancellationRequested)
            {
                ShowSetup(setup);
            }

            var look = await _engine.Pages.AppearanceAsync(_closing.Token).ConfigureAwait(true);
            var fold = await _engine.Pages.FlagAsync(StudioSetting.FoldWork, _closing.Token).ConfigureAwait(true);
            ApplyAppearance(look, fold);
            await LoadPaletteAsync(_engine.Pages).ConfigureAwait(true);
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
        Conversation.ApplyPreferences(language, fold);
        Projects.Language = language;
        Projects.RefreshLabels();
        LabelChrome();
        Label(Attach, MessageKey.Composer.AddAnAttachment);
        Label(ModelButton, MessageKey.Composer.ChooseModel);
        OpenSettings.Content = MessageCatalog.Get(MessageKey.Common.Settings, Language);
        BackLabel.Text = MessageCatalog.Get(MessageKey.Settings.BackToWorkspace, Language);
        Avalonia.Automation.AutomationProperties.SetName(CloseSettings, BackLabel.Text);
        SettingsTitle.Text = MessageCatalog.Get(MessageKey.Common.Settings, Language);
        SettingsSearch.PlaceholderText = MessageCatalog.Get(MessageKey.Settings.SearchSettings, Language);
        WorkbenchView.Header = MessageCatalog.Get(MessageKey.Common.Workbench, Language);
        SaveFile.Content = MessageCatalog.Get(MessageKey.Common.Save, Language);
        Label(FindButton, MessageKey.Chat.Search);
        FindText.PlaceholderText = MessageCatalog.Get(MessageKey.Chat.Search, Language);
        ExplorerHeading.Text = MessageCatalog.Get(MessageKey.Workbench.Explorer, Language);
        BrowserAction.Header = MessageCatalog.Get(MessageKey.Workbench.Browser, Language);
        TerminalAction.Header = MessageCatalog.Get(MessageKey.Workbench.Terminal, Language);
        ShowComposerState(Conversation.State);
        LabelSetup();
    }

    /// <summary>Opens the settings sheet and focuses its search box.</summary>
    private void OpenPreferences()
    {
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
        SettingsSearch.Focus();
    }

    /// <summary>Restores the existing session and draft after closing a full-window sheet.</summary>
    private async void ReturnToConversation()
    {
        if (SettingsPanel.IsVisible && !await MayLeaveSettingsAsync().ConfigureAwait(true))
        {
            return;
        }

        _unsavedSettings = null;
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
        _shownSettings = section;
        var ticket = ++_settingsRead;
        var parent = section.Split(':')[0];
        SettingsHeadingBlock.IsVisible = true;
        SettingsHeading.Text = MessageCatalog.Get(s_sections.First(s => s.Id == parent).Title, Language);
        SettingsDescription.Text = SectionDescription(parent);
        SettingsDescription.IsVisible = SettingsDescription.Text.Length > 0;
        SettingsUsual.Text = MessageCatalog.Get("settings.page_usual", Language);
        SettingsError.Text = string.Empty;
        SettingsIndex.IsVisible = false;
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
                        ZoomChoice(Card(column, "common.window"), look);
                    }

                    break;
                case "controls":
                    var keys = Card(column, "settings.keyboard_shortcuts");
                    ShortcutChoice(keys);
                    ShortcutReference(keys);
                    var intents = new[]
                    {
                        new SectionEntry("steer", MessageCatalog.Get("settings.steer_at_the_next_tool", Language)),
                        new SectionEntry("queue", MessageCatalog.Get(MessageKey.Composer.Queue, Language)),
                    };
                    Choice(Card(column, "settings.while_running_card"), MessageKey.Settings.MessagesSentWhileRunning, intents, QueueDuringRun ? "queue" : "steer", id =>
                    {
                        QueueDuringRun = id == "queue";
                        ShowComposerState(Conversation.State);
                        return Task.CompletedTask;
                    });
                    break;
                case "model" when api is not null:
                    var services = Card(column, "models.services");
                    var models = await api.ModelsAsync(_closing.Token).ConfigureAwait(true);
                    var providers = await api.ProvidersAsync(_closing.Token).ConfigureAwait(true);
                    foreach (var service in providers.OfType<JsonObject>())
                    {
                        var info = new Expander
                        {
                            Header = (string?)service["displayName"] ?? (string?)service["name"],
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                        };
                        var list = service["models"] is JsonArray names ? string.Join(" · ", names.Select(n => n?.ToString())) : string.Empty;
                        info.Content = Detail(string.Join(Environment.NewLine, new[] { (string?)service["baseUrl"], list }.Where(s => !string.IsNullOrWhiteSpace(s))));
                        services.Children.Add(info);
                    }

                    AddAction(services, MessageKey.Onboarding.ConnectAModelService, () =>
                    {
                        ShowSetup(new ProviderSetup(true, true));
                        return Task.CompletedTask;
                    });
                    await FlagControlAsync(services, StudioSetting.ProviderHealth, "models.automatically_check_connections_and_balances").ConfigureAwait(true);
                    var choices = models.Where(m => !m.NotAdded).Select(m => new SectionEntry(m.Ref, m.ModelAlias ?? m.Model)).ToArray();
                    var selected = models.FirstOrDefault(m => m.Default)?.Ref ?? string.Empty;
                    Choice(Card(column, "models.uses_and_context"), MessageKey.Common.DefaultModel, choices, selected, id => api.SetDefaultModelAsync(id, _closing.Token));
                    break;
                case "session" when api is not null:
                    var modes = Card(column, "settings.persona_and_presets");
                    AddAction(modes, MessageKey.Persona.SystemPersona, () => ShowSectionAsync("session:persona"));
                    var presets = new Settings.PresetWorkspace { Tag = "persona.agent_presets" };
                    modes.Children.Add(presets);
                    presets.Open(api, Language, await api.AgentPresetsAsync(_closing.Token).ConfigureAwait(true), MayLeaveSettingsAsync, DescribeError, _closing.Token);
                    if (TopLevel.GetTopLevel(presets) == this)
                    {
                        _unsavedSettings = () => presets.HasChanges;
                    }

                    var questions = Card(column, "settings.plans_and_questions");
                    var timeout = await api.AskTimeoutAsync(_closing.Token).ConfigureAwait(true);
                    var seconds = new NumericUpDown
                    {
                        Minimum = (int?)timeout["minSeconds"] ?? 0,
                        Maximum = (int?)timeout["maxSeconds"] ?? 0,
                        Value = (int?)timeout["seconds"],
                        Increment = 1,
                    };
                    AddRow(questions, "safety.question_wait_time", seconds);
                    AddAction(questions, MessageKey.Common.Save, () => api.SetAskTimeoutAsync((int)(seconds.Value ?? 0), _closing.Token));
                    break;
                case "session:persona" when api is not null:
                    var persona = Card(column, "settings.persona_and_presets");
                    AddAction(persona, MessageKey.Common.Back, () => ShowSectionAsync("session"));
                    await PersonaSettingsAsync(persona, api).ConfigureAwait(true);
                    break;
                case "tools" when api is not null:
                    await ToolSettingsAsync(Card(column, "settings.sandbox_and_execution"), api).ConfigureAwait(true);
                    await FlagControlAsync(Card(column, "safety.web"), StudioSetting.BrowserTools, "common.built_in_browser").ConfigureAwait(true);
                    break;
                case "display":
                    await FlagControlAsync(Card(column, "settings.process_card"), StudioSetting.FoldWork, "settings.fold_the_work_in_between").ConfigureAwait(true);
                    var previews = Card(column, "settings.files_and_previews_card");
                    await FlagControlAsync(previews, StudioSetting.ChangedFiles, "settings.file_changes").ConfigureAwait(true);
                    await FlagControlAsync(previews, StudioSetting.PresentFiles, "settings.ai_file_delivery_cards").ConfigureAwait(true);
                    break;
                case "storage":
                    if (api is not null)
                    {
                        var catalog = await api.MemoriesAsync(_closing.Token).ConfigureAwait(true);
                        var memories = new Settings.MemoryWorkspace { Tag = "data.memory" };
                        Card(column, "data.memory_and_usage").Children.Add(memories);
                        memories.Open(api, Language, catalog, DescribeError, _closing.Token);
                        if (TopLevel.GetTopLevel(memories) == this)
                        {
                            _unsavedSettings = () => memories.HasChanges;
                        }
                    }

                    await ArchiveSettingsAsync(Card(column, "data.session_storage")).ConfigureAwait(true);
                    break;
                case "ext" or "ext:market" when api is not null:
                    await PackageSettingsAsync(column, api, section == "ext:market").ConfigureAwait(true);
                    break;
                case "network" when api is not null:
                    await NetworkSettingsAsync(Card(column, "common.network"), api).ConfigureAwait(true);
                    var remotes = await api.RemoteHostsAsync(_closing.Token).ConfigureAwait(true);
                    if (remotes is not null)
                    {
                        var aliases = await api.RemoteCandidatesAsync(_closing.Token).ConfigureAwait(true);
                        var distros = await api.RemoteDistrosAsync(_closing.Token).ConfigureAwait(true);
                        var book = new Settings.RemoteBook { Tag = "remote.add_a_machine" };
                        Card(column, "remote.machine").Children.Add(book);
                        book.Open(api, remotes, aliases, distros, Language, DescribeError, _closing.Token);
                        if (TopLevel.GetTopLevel(book) == this)
                        {
                            var proxyDirty = _unsavedSettings;
                            _unsavedSettings = () => book.HasChanges || proxyDirty?.Invoke() == true;
                        }
                    }

                    break;
                case "account" when api is not null:
                    var account = await api.AccountAsync(_closing.Token).ConfigureAwait(true);
                    var accountState = new TextBlock
                    {
                        Text = MessageCatalog.Get((bool?)account["signedIn"] == true ? MessageKey.Settings.SignedIn : MessageKey.Common.NotSignedIn, Language),
                    };
                    AddRow(Card(column, "settings.account"), MessageKey.Settings.Account, accountState, (string?)account["user"]?["label"]);
                    var diagnostics = Card(column, "settings.version_and_diagnostics");
                    await VersionSettingsAsync(diagnostics, api).ConfigureAwait(true);
                    await FlagControlAsync(diagnostics, StudioSetting.TraceExport, "data.trace_export").ConfigureAwait(true);
                    LogsAction(diagnostics);
                    break;
                case "advanced":
                    var advanced = Card(column, "settings.not_in_this_version_yet");
                    advanced.Children.Add(Detail(MessageCatalog.Get("settings.the_items_below_have_no", Language)));
                    LogsAction(advanced);
                    break;
            }

            if (ticket == _settingsRead)
            {
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

    /// <summary>Shows the kernel's editable persona fields.</summary>
    /// <param name="content">The settings column.</param>
    /// <param name="api">The current page endpoints.</param>
    /// <returns>The persona read.</returns>
    private async Task PersonaSettingsAsync(StackPanel content, StudioPageClient api)
    {
        var persona = await api.PersonaAsync(_closing.Token).ConfigureAwait(true);
        var prefix = new TextBox { Text = (string?)persona["prefix"], AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var suffix = new TextBox { Text = (string?)persona["suffix"], AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var omit = new ToggleSwitch { IsChecked = (bool?)persona["include_runtime_context"] == false, OnContent = string.Empty, OffContent = string.Empty };
        var presets = new WrapPanel();
        foreach (var preset in persona["presets"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            var id = (string?)preset["id"];
            var name = id switch
            {
                "engineer" => MessageCatalog.Get("persona.rigorous_engineer", Language),
                "concise" => MessageCatalog.Get("persona.concise", Language),
                "mentor" => MessageCatalog.Get("persona.mentor", Language),
                "critic" => MessageCatalog.Get("persona.critic", Language),
                _ => id,
            };
            var choice = new Button { Content = name, Margin = new Thickness(0, 0, 6, 6) };
            AutomationProperties.SetAutomationId(choice, "action:persona.preset");
            choice.Click += (_, _) =>
            {
                prefix.Text = (string?)preset["prefix"];
                suffix.Text = (string?)preset["suffix"];
                omit.IsChecked = (bool?)preset["include_runtime_context"] == false;
            };
            presets.Children.Add(choice);
        }

        if (presets.Children.Count > 0)
        {
            content.Children.Add(presets);
        }

        prefix.MinHeight = 94;
        suffix.MinHeight = 94;
        AddRow(content, "persona.start_of_the_system_prompt", prefix, MessageCatalog.Get("persona.leads_the_system_prompt_ahead", Language));
        AddRow(content, "persona.additional_instructions", suffix, MessageCatalog.Get("persona.placed_after_the_built_in", Language));
        AddRow(content, "persona.do_not_inject_optional_runtime", omit, MessageCatalog.Get("persona.when_on_the_probed_environment", Language));
        var saved = ((string?)persona["prefix"] ?? string.Empty, (string?)persona["suffix"] ?? string.Empty, (bool?)persona["include_runtime_context"] == true);
        if (TopLevel.GetTopLevel(content) == this)
        {
            _unsavedSettings = () => saved != (prefix.Text ?? string.Empty, suffix.Text ?? string.Empty, omit.IsChecked != true);
        }

        AddAction(content, MessageKey.Common.Save, async () =>
        {
            var chosen = (prefix.Text ?? string.Empty, suffix.Text ?? string.Empty, omit.IsChecked != true);
            await api.SetPersonaAsync(chosen.Item1, chosen.Item2, chosen.Item3, _closing.Token).ConfigureAwait(true);
            saved = chosen;
        });
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
        AddRow(content, key, toggle);
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
            "safety.how_new_conversations_start" => "default-mode.plan",
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
