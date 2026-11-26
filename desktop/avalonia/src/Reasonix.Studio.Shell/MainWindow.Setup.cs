using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The service shortcuts offered by the reference setup screen.</summary>
    private static readonly (string Label, string Name, string Address, string Hint, string? Mark)[] s_setupServices =
    [
        ("DeepSeek", "deepseek", "https://api.deepseek.com", "onboarding.deepseek_hint", "deepseek"),
        ("onboarding.siliconflow", "siliconflow", "https://api.siliconflow.cn/v1", "onboarding.siliconflow_hint", null),
        ("onboarding.moonshot", "moonshot", "https://api.moonshot.cn/v1", "onboarding.moonshot_hint", "kimi"),
        ("onboarding.zhipu", "zhipu", "https://open.bigmodel.cn/api/paas/v4", "onboarding.zhipu_hint", "zhipu"),
        ("MiMo", "mimo", "https://api.xiaomimimo.com/v1", "onboarding.mimo_hint", "mimo"),
        ("LongCat", "longcat", "https://api.longcat.chat/openai", "onboarding.longcat_hint", "longcat"),
        ("onboarding.relay_self_hosted", string.Empty, string.Empty, "onboarding.relay_hint", null),
    ];

    /// <summary>The service state that caused setup to open.</summary>
    private ProviderSetup? _setup;

    /// <summary>Discovery results are valid only for the endpoint and key that produced them.</summary>
    private ProviderProbe? _probe;

    /// <summary>Opens setup for an existing provider or a new model service.</summary>
    /// <param name="setup">State returned by the active kernel.</param>
    internal void ShowSetup(ProviderSetup setup)
    {
        CloseComposerMenus();
        var entering = !SetupPanel.IsVisible || !WelcomeCard.IsVisible;
        _setup = setup;
        ProjectStep.IsVisible = false;
        SetupServices.Children.Clear();
        foreach (var service in s_setupServices)
        {
            var radio = CreateServiceChoice(service.Label, service.Hint, service.Mark);
            AutomationProperties.SetAutomationId(radio, "action:onboarding.service");
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked != true)
                {
                    return;
                }

                ServiceKey.Text = string.Empty;
                ServiceAddress.Text = service.Address;
                ServiceName.Text = service.Name;
                ServiceAddress.IsReadOnly = service.Address.Length > 0;
                ServiceAddress.IsVisible = ServiceName.IsVisible = !ServiceAddress.IsReadOnly;
                ResetProbe();
                LabelServiceNote();
            };
            SetupServices.Children.Add(radio);
        }

        SetupServices.Children.OfType<RadioButton>().First().IsChecked = true;
        WelcomeCard.IsVisible = true;
        SetupForm.IsVisible = false;
        ServiceKey.Text = string.Empty;
        ResetProbe();
        SettingsPanel.IsVisible = false;
        LayoutRoot.IsVisible = false;
        SetupPanel.IsVisible = true;
        ServiceFields.IsVisible = setup.NoProvider;
        SetSetupError(setup.Error);
        LabelSetup();
        if (entering)
        {
            SetupDemo.Begin();
            EnterWelcome();
        }

        BeginSetup.Focus();
    }

    /// <summary>Connects the onboarding controls.</summary>
    private void InitializeSetup()
    {
        ProjectStep.Configure(
            () => new Host.AvaloniaHost(this).PickFolderAsync(_workspaceRoot),
            path => OpenTargetCoreAsync(new Pages.Rail.SessionOpenTarget(string.Empty, path, string.Empty), register: true, animateSetupDeparture: true),
            () => ReturnToConversation(animateSetupDeparture: true),
            () => ReturnToConversation(),
            DescribeError);
        SetupPanel.PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty && !SetupPanel.IsVisible)
            {
                _setupChoicesMotion?.Cancel();
                CancelWelcomeEntrance();
                SetupDemo.End();
            }
        };
        SetupServices.SizeChanged += (_, e) => SetupServices.Columns = Math.Max(1, (int)((e.NewSize.Width + 8) / 128));
        SetupPanel.SizeChanged += (_, e) =>
        {
            var wide = e.NewSize.Width >= 900;
            SetupDemo.IsVisible = wide;
            WelcomeLayout.ColumnSpacing = wide ? 32 : 0;
            WelcomeCard.MaxWidth = wide ? 900 : 560;
            WelcomeCard.Height = wide ? 394 : 374;
            WelcomeCard.Padding = wide ? new Avalonia.Thickness(42, 36, 42, 34) : new Avalonia.Thickness(22, 26, 22, 24);
            SetupPanel.RowDefinitions[1].MinHeight = !wide && WelcomeCard.IsVisible ? 374 : 0;
        };
        BeginSetup.Click += (_, _) =>
        {
            CancelWelcomeEntrance();
            SetupDemo.End();
            WelcomeCard.IsVisible = false;
            SetupForm.IsVisible = true;
            SetupPanel.RowDefinitions[1].MinHeight = 0;
            EnterView(SetupCard, slideX: 40);
            EnterSetupChoices(SetupServices);
            ServiceKey.Focus();
        };
        SetupBack.Click += (_, _) =>
        {
            if (_probe is not null)
            {
                ResetProbe();
                LabelSetup();
                EnterView(SetupCard, slideX: -40);
                EnterSetupChoices(SetupServices);
                ServiceKey.Focus();
                return;
            }

            _setupChoicesMotion?.Cancel();
            SetupForm.IsVisible = false;
            WelcomeCard.IsVisible = true;
            SetupPanel.RowDefinitions[1].MinHeight = SetupPanel.Bounds.Width < 900 ? 374 : 0;
            SetupDemo.Begin();
            EnterWelcome();
            BeginSetup.Focus();
        };
        SkipWelcome.Click += (_, _) => ReturnToConversation();
        DeferSetup.Click += (_, _) => ReturnToConversation();
        ConnectProvider.Click += OnConnectProvider;
        ServiceAddress.TextChanged += (_, _) =>
        {
            ServiceKey.Text = string.Empty;
            ResetProbe();
        };
        ServiceName.TextChanged += (_, _) => UpdateConnectButton();
        ServiceModels.SelectionChanged += (_, _) => UpdateConnectButton();
        ServiceKey.TextChanged += (_, _) => ResetProbe();
        LabelSetup();
    }

    /// <summary>Translates the visible onboarding step.</summary>
    private void LabelSetup()
    {
        WelcomeTitle.Text = MessageCatalog.Get(MessageKey.Onboarding.HandItATask, Language);
        WelcomeDescription.Text = MessageCatalog.Get(MessageKey.Onboarding.WhatItDoesAndShows, Language);
        BeginSetup.Content = MessageCatalog.Get(MessageKey.Onboarding.SetUpReasonix, Language);
        SkipWelcome.Content = MessageCatalog.Get(MessageKey.Onboarding.NotNow, Language);
        SetupTime.Text = MessageCatalog.Get(MessageKey.Onboarding.AboutAMinute, Language);
        SetupFoot.Text = MessageCatalog.Get(MessageKey.Onboarding.LocalFirstCredentialsStayIn, Language);
        SetupDemo.Label(Language);
        SetupProgress.Show(_probe is null ? 0 : 1, Language);
        SetupTitle.Text = MessageCatalog.Get(MessageKey.Onboarding.ConnectOneModelService, Language);
        SetupDescription.Text = MessageCatalog.Get(MessageKey.Onboarding.PickOneAndFillTheKey, Language);
        SetupModelLabel.Text = MessageCatalog.Get(MessageKey.Onboarding.PickADefaultModel, Language);
        SetupModelNote.Text = MessageCatalog.Get(MessageKey.Onboarding.EachConversationCanSwitch, Language);
        SetupBack.Content = MessageCatalog.Get(MessageKey.Common.Back, Language);
        foreach (var radio in SetupServices.Children.OfType<RadioButton>())
        {
            LabelServiceChoice(radio);
        }

        LabelServiceNote();
        ConnectLabel.Text = MessageCatalog.Get(_probe is null ? MessageKey.Onboarding.Connect : MessageKey.Onboarding.Continue, Language);
        AutomationProperties.SetName(ConnectProvider, ConnectLabel.Text);
        DeferSetup.Content = MessageCatalog.Get(MessageKey.Onboarding.NotNow, Language);
        ServiceAddress.PlaceholderText = MessageCatalog.Get("onboarding.service_address", Language);
        ServiceName.PlaceholderText = MessageCatalog.Get(MessageKey.Common.Name, Language);
        Label(ServiceAddress, "onboarding.service_address");
        Label(ServiceName, MessageKey.Common.Name);
        Label(ServiceModels, MessageKey.Common.DefaultModel);
        foreach (var radio in ModelOptions.Children.OfType<RadioButton>())
        {
            LabelModelChoice(radio, _probe!);
        }
    }

    /// <summary>Invalidates a model list when its credential or service address changes.</summary>
    private void ResetProbe()
    {
        _probe = null;
        ServiceProtocol.ItemsSource = null;
        ServiceProtocol.IsVisible = false;
        ServiceModels.ItemsSource = null;
        ServiceModels.IsVisible = false;
        ModelOptions.IsVisible = false;
        SetupModelField.IsVisible = false;
        SetupTitle.IsVisible = SetupDescription.IsVisible = true;
        ModelOptions.Children.Clear();
        ServiceFields.IsVisible = _setup?.NoProvider == true;
        ServiceKeyField.IsVisible = ServiceKey.IsVisible = true;
        SetupServiceNote.IsVisible = _setup?.NoProvider == true && ServiceAddress.IsReadOnly;
        UpdateConnectButton();
        ConnectLabel.Text = MessageCatalog.Get(MessageKey.Onboarding.Connect, Language);
        AutomationProperties.SetName(ConnectProvider, ConnectLabel.Text);
        SetupFound.IsVisible = false;
        SetupFound.Classes.Remove("enter");
    }

    /// <summary>Switches the native controls and event subscription to the chosen project.</summary>
    /// <param name="path">The user-selected folder.</param>
    /// <returns>The transition to the new conversation.</returns>
    /// <param name="register">Whether to register a newly picked folder.</param>
    /// <param name="session">Transcript to activate, or null to start a new task.</param>
    /// <param name="current">Checks that another navigation has not superseded this one.</param>
    /// <param name="remoteHost">Optional machine owning the project.</param>
    private async System.Threading.Tasks.Task ActivateProjectAsync(string path, bool register, string? session = null, Func<bool>? current = null, string? remoteHost = null)
    {
        await OpenTargetCoreAsync(new Pages.Rail.SessionOpenTarget(remoteHost ?? string.Empty, path, session ?? string.Empty), register, current).ConfigureAwait(true);
    }

    /// <summary>Enables only a fully specified connection or a discovered model.</summary>
    private void UpdateConnectButton()
    {
        ConnectProvider.IsEnabled = DeferSetup.IsEnabled && !string.IsNullOrWhiteSpace(ServiceKey.Text)
            && (_setup?.NoProvider != true || (!string.IsNullOrWhiteSpace(ServiceAddress.Text) && !string.IsNullOrWhiteSpace(ServiceName.Text)))
            && (_probe is null || ServiceModels.SelectedItem is string);
    }

    /// <summary>Discovers models, commits the user's choice and reads back the active session.</summary>
    /// <param name="sender">The connect or continue button.</param>
    /// <param name="e">The click.</param>
    private async void OnConnectProvider(object? sender, RoutedEventArgs e)
    {
        if (_engine is null || _setup is null || !ConnectProvider.IsEnabled || string.IsNullOrWhiteSpace(ServiceKey.Text))
        {
            return;
        }

        SetSetupError(null);
        ConnectProvider.IsEnabled = false;
        DeferSetup.IsEnabled = false;
        SetupBack.IsEnabled = false;
        var discovering = _probe is null && _setup.NoProvider;
        SetConnectFeedback(checking: discovering);
        ConnectLabel.Text = MessageCatalog.Get(discovering || _probe is null ? "onboarding.checking_the_key" : "common.saving_2", Language);
        AutomationProperties.SetName(ConnectProvider, ConnectLabel.Text);
        ServiceFields.IsEnabled = false;
        ServiceKey.IsEnabled = false;
        ServiceModels.IsEnabled = false;
        ServiceProtocol.IsEnabled = ModelOptions.IsEnabled = false;
        try
        {
            var key = ServiceKey.Text.Trim();
            var api = _engine.Pages;
            if (!_setup.NoProvider)
            {
                await api.SaveProviderKeyAsync(key, _closing.Token).ConfigureAwait(true);
            }
            else if (_probe is null)
            {
                var probe = await api.ProbeAsync(ServiceAddress.Text?.Trim() ?? string.Empty, key, _closing.Token).ConfigureAwait(true);
                _probe = probe;
                var kinds = (probe.Kinds ?? []).Append(probe.Kind).Distinct(StringComparer.Ordinal).ToArray();
                if (!ServiceAddress.IsReadOnly && (probe.Ambiguous || kinds.Length > 1))
                {
                    try
                    {
                        var catalog = await api.ProtocolsAsync(_closing.Token).ConfigureAwait(true);
                        kinds = kinds.Concat(catalog.Select(p => (string?)p?["kind"]).OfType<string>()).Distinct(StringComparer.Ordinal).ToArray();
                    }
                    catch (KernelRefusal)
                    {
                        // Older hosts still advertise the usable protocols in their probe.
                    }
                }

                ShowConnectedProbe(probe);
                await System.Threading.Tasks.Task.Delay(750, _closing.Token).ConfigureAwait(true);
                if (!SetupPanel.IsVisible || !SetupForm.IsVisible || !ReferenceEquals(_probe, probe))
                {
                    return;
                }

                SetupFound.Classes.Remove("enter");

                ServiceProtocol.ItemsSource = kinds;
                ServiceProtocol.SelectedItem = probe.Kind;
                ServiceProtocol.IsVisible = !ServiceAddress.IsReadOnly && (probe.Ambiguous || kinds.Length > 1);
                Label(ServiceProtocol, "onboarding.how_it_is_spoken_to");
                ServiceModels.ItemsSource = probe.Models;
                ServiceModels.SelectedItem = probe.Default ?? probe.Models.FirstOrDefault();
                ServiceModels.IsVisible = probe.Models.Length > 8;
                ModelOptions.IsVisible = !ServiceModels.IsVisible;
                ModelOptions.Children.Clear();
                if (ModelOptions.IsVisible)
                {
                    foreach (var name in probe.Models)
                    {
                        var pick = CreateModelChoice(name, Equals(ServiceModels.SelectedItem, name));
                        AutomationProperties.SetAutomationId(pick, "action:onboarding.model");
                        pick.IsCheckedChanged += (_, _) =>
                        {
                            if (pick.IsChecked == true)
                            {
                                ServiceModels.SelectedItem = name;
                            }
                        };
                        ModelOptions.Children.Add(pick);
                    }
                }

                SetupTitle.IsVisible = SetupDescription.IsVisible = false;
                ServiceKeyField.IsVisible = ServiceKey.IsVisible = false;
                SetupServiceNote.IsVisible = ServiceFields.IsVisible = false;
                SetupModelField.IsVisible = true;
                LabelSetup();
                EnterView(SetupCard, slideX: 40);
                EnterSetupChoices(ModelOptions);
                (ServiceModels.IsVisible ? (Control)ServiceModels : ModelOptions.Children.FirstOrDefault())?.Focus();
                return;
            }
            else if (ServiceModels.SelectedItem is string model)
            {
                await api.ConfigureProviderAsync(
                    ServiceName.Text?.Trim() ?? string.Empty,
                    ServiceAddress.Text?.Trim() ?? string.Empty,
                    key,
                    _probe with
                    {
                        Kind = ServiceProtocol.SelectedItem as string ?? _probe.Kind,
                    },
                    model,
                    _closing.Token).ConfigureAwait(true);
            }
            else
            {
                return;
            }

            var state = await api.ProviderSetupAsync(_closing.Token).ConfigureAwait(true);
            if (state is { Required: true })
            {
                ShowSetup(state);
                return;
            }

            if (await _engine.StatusAsync(_closing.Token).ConfigureAwait(true) is { } status)
            {
                SetModelLabel(status);
            }

            ServiceKey.Text = string.Empty;
            SetupForm.IsVisible = false;
            ProjectStep.IsVisible = true;
            ProjectStep.Begin(Language);
            EnterView(ProjectStep, slideX: 40);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            SetSetupError(DescribeError(error));
        }
        finally
        {
            SetConnectFeedback(checking: false);
            DeferSetup.IsEnabled = true;
            SetupBack.IsEnabled = true;
            ServiceFields.IsEnabled = true;
            ServiceKey.IsEnabled = true;
            ServiceModels.IsEnabled = true;
            ServiceProtocol.IsEnabled = ModelOptions.IsEnabled = true;
            UpdateConnectButton();
            LabelSetup();
        }
    }
}
