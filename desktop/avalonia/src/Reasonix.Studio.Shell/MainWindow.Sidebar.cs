using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Host;
using System;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Connects the sidebar's project picker and anchored settings shortcuts.</summary>
    private void InitializeSidebar()
    {
        Projects.RemoteOpened += async (host, root, path) =>
        {
            var key = System.Text.Json.JsonSerializer.Serialize(new[] { host, path.Length > 0 ? path : root });
            var ticket = _switch.Begin(key);
            try
            {
                await ActivateProjectAsync(root, false, path.Length > 0 ? path : null, () => _switch.IsCurrent(ticket), host).ConfigureAwait(true);
            }
            catch (Exception error)
            {
                if (_switch.IsCurrent(ticket))
                {
                    ShowRefusal(error);
                }
            }
            finally
            {
                _switch.End(ticket);
            }
        };
        Projects.RemoteWritten += async (host, path, action, value) =>
        {
            try
            {
                if (_engine is not null)
                {
                    await _engine.Pages.WriteRemoteSessionAsync(host, path, action, value, _closing.Token).ConfigureAwait(true);
                    await LoadTreeAsync().ConfigureAwait(true);
                }
            }
            catch (Exception error)
            {
                ShowRefusal(error);
            }
        };
        Projects.RemoteRefreshRequested += async () => await LoadTreeAsync().ConfigureAwait(true);
        _sidebarRefresh.Tick += async (_, _) =>
        {
            _sidebarRefresh.Stop();
            try
            {
                await LoadRemoteSidebarAsync(new Contracts.Tree.TreeWindow { Query = RailSearch.Text?.Trim() ?? string.Empty, HideLegacy = HideLegacySessions }, _treeRead).ConfigureAwait(true);
            }
            finally
            {
                if (!_closing.IsCancellationRequested)
                {
                    _sidebarRefresh.Start();
                }
            }
        };
        Closed += (_, _) => _sidebarRefresh.Stop();
        LayoutRoot.SizeChanged += (_, _) =>
        {
            var compact = LayoutRoot.Bounds.Width <= 760;
            BrandTag.IsVisible = !compact;
            BrandLogo.Margin = new Avalonia.Thickness(compact ? 19 : 5, 0, 2, 0);
        };
        Projects.AddRequested += async () =>
        {
            var path = await new AvaloniaHost(this).PickFolderAsync(_workspaceRoot).ConfigureAwait(true);
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    await ActivateProjectAsync(path, true).ConfigureAwait(true);
                }
                catch (Exception error)
                {
                    ShowRefusal(error);
                }
            }
        };
        SidebarAccount.Click += (_, _) => ShowSidebarSection("account");
        SidebarModels.Click += (_, _) => ShowSidebarSection("model");
        SidebarTools.Click += (_, _) => ShowSidebarSection("ext");
        SidebarConnections.Click += (_, _) => ShowSidebarSection("network");
    }

    /// <summary>Updates the anchored footer menu without replacing the controls.</summary>
    private void LabelSidebar()
    {
        SidebarAccount.Header = MessageCatalog.Get(MessageKey.Settings.Account, Language);
        SidebarModels.Header = MessageCatalog.Get(MessageKey.Common.Models, Language);
        SidebarTools.Header = MessageCatalog.Get("ext.extension", Language);
        SidebarConnections.Header = MessageCatalog.Get("usage.connections", Language);
        AutomationProperties.SetName(OpenAccount, MessageCatalog.Get("session.sidebar_shortcuts", Language));
        Projects.RefreshLabels();
    }

    /// <summary>Opens the existing settings route from the sidebar's shortcut menu.</summary>
    /// <param name="id">Canonical settings section.</param>
    private void ShowSidebarSection(string id)
    {
        OpenPreferences();
        foreach (var entry in SettingsNav.Items)
        {
            if (entry is SectionEntry section && section.Id == id)
            {
                SettingsNav.SelectedItem = entry;
                break;
            }
        }
    }
}
