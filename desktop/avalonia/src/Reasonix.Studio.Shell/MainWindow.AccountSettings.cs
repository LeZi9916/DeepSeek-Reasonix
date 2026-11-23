using Avalonia.Controls;
using Avalonia.Layout;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Host;
using Reasonix.Studio.Shell.Settings;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Cancels the mounted account page when it no longer owns the settings sheet.</summary>
    private CancellationTokenSource? _accountSettingsLifetime;

    /// <summary>Mounts independent account and desktop diagnostic groups.</summary>
    /// <param name="column">The account settings content.</param><param name="api">The active session's runtime.</param>
    /// <returns>The initial reads for the mounted groups.</returns>
    private async Task ShowAccountSettingsAsync(StackPanel column, StudioPageClient api)
    {
        _accountSettingsLifetime = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        var token = _accountSettingsLifetime.Token;
        var machine = _launchOwner?.Pages ?? api;
        var host = new AvaloniaHost(this);
        var access = new AccountAccess(api, Language, host.OpenExternalAsync, DescribeError, token);
        var account = Card(column, "settings.account");
        if (account.Parent is Border border)
        {
            border.Tag = null;
        }

        account.Children.Add(new SettingsGroup("settings.account", "settings.account_hint", access, Language, scope: "settings.account"));
        var diagnostics = Card(column, "settings.version_and_diagnostics");
        var versions = new StackPanel { Spacing = 11 };
        diagnostics.Children.Add(new SettingsGroup("common.version", "settings.versions_hint", versions, Language));
        var logs = new StudioLogs(Language, host.RevealLogsAsync);
        diagnostics.Children.Add(new SettingsGroup("about.logs_and_abnormal_exit_records", "settings.logs_hint", logs, Language));
        var trace = new TraceExportSettings(api, Language, DescribeError, token);
        var traceGroup = new SettingsGroup("data.trace_export", null, trace, Language, scope: "settings.applies_to_new_sessions") { IsVisible = false };
        diagnostics.Children.Add(traceGroup);
        var traceRead = trace.OpenAsync();
        await Task.WhenAll(
            access.ReloadAsync(),
            ShowVersionSummaryAsync(versions, machine, token),
            logs.LoadAsync(machine, Language, DescribeError, token),
            traceRead).ConfigureAwait(true);
        if (await traceRead.ConfigureAwait(true))
        {
            traceGroup.IsVisible = true;
        }
        else
        {
            diagnostics.Children.Remove(traceGroup);
        }

        if (!token.IsCancellationRequested)
        {
            AddAccountAbout(column, host);
        }
    }

    /// <summary>Stops a pending device sign-in when leaving its settings page.</summary>
    private void StopAccountSettings()
    {
        if (_accountSettingsLifetime is { } lifetime)
        {
            _accountSettingsLifetime = null;
            lifetime.Cancel();
            lifetime.Dispose();
        }
    }

    /// <summary>Draws the current version and keeps the existing refresh action on installed builds.</summary>
    /// <param name="body">The contents of the version group.</param><param name="api">The desktop host.</param>
    /// <param name="token">Cancels reads on departure.</param><returns>The first version read.</returns>
    private Task ShowVersionSummaryAsync(StackPanel body, StudioPageClient api, CancellationToken token)
    {
        var current = new TextBlock
        {
            [!TextBlock.FontFamilyProperty] = StudioTypography.Resource("StudioMonoFamily"),
            FontSize = 22,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            LineHeight = 22,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        var label = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Avalonia.Thickness(0, 0, 0, 1) };
        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("MutedBrush"));
        var status = Detail(MessageCatalog.Get("about.loading_versions", Language));
        var present = new WrapPanel { ItemSpacing = 9, Children = { current, label }, IsVisible = false };
        var reload = new Button { Content = MessageCatalog.Get("common.reload", Language), HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };
        Avalonia.Automation.AutomationProperties.SetAutomationId(reload, "action:versions.reload");
        reload.Click += async (_, _) => await ReadAsync(refresh: true).ConfigureAwait(true);
        body.Children.Add(present);
        body.Children.Add(status);
        body.Children.Add(reload);
        return ReadAsync(refresh: false);

        async Task ReadAsync(bool refresh)
        {
            reload.IsEnabled = false;
            try
            {
                var data = await api.VersionsAsync(refresh, token).ConfigureAwait(true);
                token.ThrowIfCancellationRequested();
                if (data is null)
                {
                    status.Text = MessageCatalog.Get("about.this_is_a_development_build", Language);
                    status.IsVisible = true;
                    return;
                }

                current.Text = (string?)data["current"] is { Length: > 0 } version ? version : "dev";
                label.Text = MessageCatalog.Get(current.Text == "dev" ? "about.local_build" : "about.current_version", Language);
                present.IsVisible = true;
                status.Text = (string?)data["err"] is { Length: > 0 } error ? error : (string?)data["latest"];
                status.IsVisible = !string.IsNullOrWhiteSpace(status.Text) && status.Text != current.Text;
                reload.IsVisible = current.Text != "dev" || status.IsVisible;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                status.Text = DescribeError(error);
                status.IsVisible = reload.IsVisible = true;
            }
            finally
            {
                reload.IsEnabled = true;
            }
        }
    }
}
