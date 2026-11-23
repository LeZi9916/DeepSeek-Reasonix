using Avalonia;
using Avalonia.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Switches between installed extensions and searchable marketplace records.</summary>
    /// <param name="column">The section body.</param>
    /// <param name="api">The kernel supplying the inventories.</param>
    /// <param name="market">Whether the discovery tab is selected.</param>
    /// <returns>The active tab read.</returns>
    private async Task PackageSettingsAsync(StackPanel column, StudioPageClient api, bool market)
    {
        var toolbar = new Settings.ExtensionsToolbar();
        column.Children.Add(toolbar);
        if (!market)
        {
            var installed = new Settings.InstalledExtensionsView();
            toolbar.Open(Language, false, ChangeTabAsync, installed.AddSourceAsync, installed.AddServiceAsync);
            column.Children.Add(installed);
            await installed.OpenAsync(
                api,
                Language,
                DescribeError,
                (scope, choices) => toolbar.ShowScope(scope, choices, installed.ChangeScopeAsync),
                _closing.Token).ConfigureAwait(true);
            return;
        }

        toolbar.Open(
            Language,
            true,
            ChangeTabAsync,
            folder => OpenInstalledActionAsync(list => list.AddSourceAsync(folder)),
            () => OpenInstalledActionAsync(list => list.AddServiceAsync()));
        var scope = (await api.ExtensionServicesAsync(null, _closing.Token).ConfigureAwait(true))?["scope"] as JsonObject;
        toolbar.ShowScope(scope, [], null);
        var marketplace = new Settings.ExtensionMarketplaceView { Tag = "ext.community_market" };
        column.Children.Add(marketplace);
        await marketplace.OpenAsync(
            api,
            Language,
            DescribeError,
            async (slug, inspect) =>
            {
                var detail = new Settings.MarketInstall();
                marketplace.ShowDetail(detail);
                await detail.OpenAsync(
                    api,
                    slug,
                    Language,
                    DescribeError,
                    async () =>
                    {
                        marketplace.ShowDetail(null);
                        await marketplace.ReloadAsync().ConfigureAwait(true);
                    },
                    _closing.Token,
                    inspect,
                    () => ShowSectionAsync("account"),
                    ShowInstalledAsync).ConfigureAwait(true);
            },
            () => ShowSectionAsync("account"),
            _closing.Token,
            ShowInstalledAsync,
            toolbar.SetApplying).ConfigureAwait(true);

        Task ShowInstalledAsync(string kind, string name) => OpenInstalledActionAsync(list =>
        {
            FocusSetting(list.InstalledTarget(kind, name));
            return Task.CompletedTask;
        });

        async Task OpenInstalledActionAsync(Func<Settings.InstalledExtensionsView, Task> action)
        {
            await ShowSectionAsync("ext").ConfigureAwait(true);
            if (SettingsContent.Content is StackPanel active && active.Children.OfType<Settings.InstalledExtensionsView>().FirstOrDefault() is { } list)
            {
                await action(list).ConfigureAwait(true);
            }
        }

        Task ChangeTabAsync(bool discover) => discover == market
            ? Task.CompletedTask
            : ShowSectionAsync(discover ? "ext:market" : "ext");
    }
}
