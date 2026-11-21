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
    private static readonly (string Label, string Name, string Address)[] s_setupServices =
    [
        ("DeepSeek", "deepseek", "https://api.deepseek.com"),
        ("onboarding.siliconflow", "siliconflow", "https://api.siliconflow.cn/v1"),
        ("onboarding.moonshot", "moonshot", "https://api.moonshot.cn/v1"),
        ("onboarding.zhipu", "zhipu", "https://open.bigmodel.cn/api/paas/v4"),
        ("MiMo", "mimo", "https://api.xiaomimimo.com/v1"),
        ("LongCat", "longcat", "https://api.longcat.chat/openai"),
        ("onboarding.relay_self_hosted", string.Empty, string.Empty),
    ];

    /// <summary>The service state that caused setup to open.</summary>
    private ProviderSetup? _setup;

    /// <summary>Discovery results are valid only for the endpoint and key that produced them.</summary>
    private ProviderProbe? _probe;

    /// <summary>Connects the onboarding controls.</summary>
    private void InitializeSetup()
    {
        SetupPanel.SizeChanged += (_, e) =>
        {
            var wide = e.NewSize.Width >= 900;
            SetupDemo.IsVisible = wide;
            WelcomeCard.MaxWidth = wide ? 900 : 560;
            WelcomeCard.Height = wide ? 394 : 374;
            WelcomeCard.Padding = wide ? new Avalonia.Thickness(42, 36, 42, 34) : new Avalonia.Thickness(22, 26, 22, 24);
            SetupPanel.RowDefinitions[1].MinHeight = !wide && WelcomeCard.IsVisible ? 374 : 0;
        };
        BeginSetup.Click += (_, _) =>
        {
            WelcomeCard.IsVisible = false;
            SetupForm.IsVisible = true;
            SetupPanel.RowDefinitions[1].MinHeight = 0;
            ServiceKey.Focus();
        };
        SetupBack.Click += (_, _) =>
        {
            if (_probe is not null)
            {
                ResetProbe();
                LabelSetup();
                ServiceKey.Focus();
                return;
            }

            SetupForm.IsVisible = false;
            WelcomeCard.IsVisible = true;
            SetupPanel.RowDefinitions[1].MinHeight = SetupPanel.Bounds.Width < 900 ? 374 : 0;
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
        DemoAsk.Text = MessageCatalog.Get(MessageKey.Onboarding.DemoAsk, Language);
        DemoThinking.Text = MessageCatalog.Get(MessageKey.Onboarding.DemoThinking, Language);
        DemoRead.Text = MessageCatalog.Get(MessageKey.Onboarding.DemoRead, Language) + "  scripts/export.py";
        DemoEdit.Text = MessageCatalog.Get(MessageKey.Onboarding.DemoEdit, Language) + "  scripts/export.py    +12 −3";
        DemoRun.Text = MessageCatalog.Get(MessageKey.Onboarding.DemoRun, Language) + "  pytest";
        DemoDone.Text = MessageCatalog.Get(MessageKey.Onboarding.DemoDone, Language);
        SetupTitle.Text = MessageCatalog.Get(_probe is null ? "onboarding.connect_one_model_service" : MessageKey.Onboarding.ChooseTheDefaultModel, Language);
        SetupDescription.Text = _probe is null
            ? MessageCatalog.Get("onboarding.pick_one_and_fill_the_key", Language)
            : MessageCatalog.Get("onboarding.connected_to_name_n_models", Language, ("name", ServiceName.Text ?? string.Empty), ("n", _probe.Models.Length.ToString(System.Globalization.CultureInfo.CurrentCulture)));
        SetupBack.Content = MessageCatalog.Get(MessageKey.Common.Back, Language);
        foreach (var radio in SetupServices.Children.OfType<RadioButton>())
        {
            var label = (string)radio.Tag!;
            radio.Content = label.StartsWith("onboarding.", StringComparison.Ordinal) ? MessageCatalog.Get(label, Language) : label;
        }

        ConnectProvider.Content = MessageCatalog.Get(_probe is null ? MessageKey.Onboarding.Connect : MessageKey.Onboarding.Continue, Language);
        DeferSetup.Content = MessageCatalog.Get(MessageKey.Onboarding.NotNow, Language);
        ServiceAddress.PlaceholderText = MessageCatalog.Get("onboarding.service_address", Language);
        ServiceName.PlaceholderText = MessageCatalog.Get(MessageKey.Common.Name, Language);
        Label(ServiceAddress, "onboarding.service_address");
        Label(ServiceName, MessageKey.Common.Name);
        Label(ServiceModels, MessageKey.Common.DefaultModel);
    }

    /// <summary>Opens setup for an existing provider or a new model service.</summary>
    /// <param name="setup">State returned by the active kernel.</param>
    private void ShowSetup(ProviderSetup setup)
    {
        _setup = setup;
        SetupServices.Children.Clear();
        foreach (var service in s_setupServices)
        {
            var radio = new RadioButton { Tag = service.Label, Margin = new Avalonia.Thickness(0, 0, 6, 6) };
            radio.Classes.Add("segmented");
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
                ResetProbe();
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
        SetupError.IsVisible = !string.IsNullOrEmpty(setup.Error);
        SetupError.Text = setup.Error;
        LabelSetup();
        BeginSetup.Focus();
    }

    /// <summary>Invalidates a model list when its credential or service address changes.</summary>
    private void ResetProbe()
    {
        _probe = null;
        ServiceModels.ItemsSource = null;
        ServiceModels.IsVisible = false;
        ServiceFields.IsVisible = _setup?.NoProvider == true;
        ServiceKey.IsVisible = true;
        UpdateConnectButton();
        ConnectProvider.Content = MessageCatalog.Get(MessageKey.Onboarding.Connect, Language);
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

        SetupError.IsVisible = false;
        ConnectProvider.IsEnabled = false;
        DeferSetup.IsEnabled = false;
        SetupBack.IsEnabled = false;
        ConnectProvider.Content = MessageCatalog.Get("onboarding.checking_the_key", Language);
        ServiceFields.IsEnabled = false;
        ServiceKey.IsEnabled = false;
        ServiceModels.IsEnabled = false;
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
                ServiceModels.ItemsSource = probe.Models;
                ServiceModels.SelectedItem = probe.Default ?? probe.Models.FirstOrDefault();
                ServiceModels.IsVisible = true;
                ServiceKey.IsVisible = false;
                ServiceFields.IsVisible = false;
                LabelSetup();
                ServiceModels.Focus();
                return;
            }
            else if (ServiceModels.SelectedItem is string model)
            {
                await api.ConfigureProviderAsync(
                    ServiceName.Text?.Trim() ?? string.Empty,
                    ServiceAddress.Text?.Trim() ?? string.Empty,
                    key,
                    _probe,
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

            ReturnToConversation();
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            SetupError.Text = DescribeError(error);
            SetupError.IsVisible = true;
        }
        finally
        {
            DeferSetup.IsEnabled = true;
            SetupBack.IsEnabled = true;
            ServiceFields.IsEnabled = true;
            ServiceKey.IsEnabled = true;
            ServiceModels.IsEnabled = true;
            UpdateConnectButton();
            LabelSetup();
        }
    }
}
