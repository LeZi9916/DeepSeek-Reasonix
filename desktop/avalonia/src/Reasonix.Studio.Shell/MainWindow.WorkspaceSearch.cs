using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell.Workspace;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The native global search overlay.</summary>
    private WorkspaceSearchPanel? _workspaceSearch;

    /// <summary>The control that opened search, restored when search closes.</summary>
    private Control? _workspaceSearchOpener;

    /// <summary>Connects sidebar search to the same global palette as Ctrl+K.</summary>
    private void InitializeWorkspaceSearch()
    {
        InitializeWorkspaceChrome();
        AddHandler(
            KeyDownEvent,
            (_, e) =>
            {
                if (!e.Handled && e.Key == Key.K && e.KeyModifiers == (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control))
                {
                    OpenWorkspaceSearch();
                    e.Handled = true;
                }
            },
            RoutingStrategies.Tunnel);
        RailSearch.AddHandler(
            PointerPressedEvent,
            (_, e) =>
            {
                OpenWorkspaceSearch();
                e.Handled = true;
            },
            RoutingStrategies.Tunnel);
        Projects.ActionRequested += async action => await RunWorkspaceActionAsync(action).ConfigureAwait(true);
    }

    /// <summary>Opens global search with canonical local and remote sessions and native actions.</summary>
    private void OpenWorkspaceSearch()
    {
        if (_workspaceSearch is { IsVisible: true })
        {
            _workspaceSearch.Hide();
            return;
        }

        if (ScaledWindow.Child is not Grid host)
        {
            return;
        }

        if (_workspaceSearch is null)
        {
            _workspaceSearch = new WorkspaceSearchPanel();
            _workspaceSearch.ZIndex = 1000;
            host.Children.Add(_workspaceSearch);
            _workspaceSearch.Closed += () => (_workspaceSearchOpener ?? Composer).Focus();
            _workspaceSearch.Recent = _workspacePreferences.PaletteRecent;
            _workspaceSearch.RecentChanged += recent =>
            {
                _workspacePreferences = _workspacePreferences with
                {
                    PaletteRecent = recent,
                };
                if (_connectOnOpen)
                {
                    _workspacePreferences.Save();
                }
            };
        }

        _workspaceSearchOpener = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        var entries = new List<WorkspaceSearchHit>();
        void Action(string key, string detail, Action run, string keywords = "") => entries.Add(new WorkspaceSearchHit("actions", MessageCatalog.Get(key, Language), detail, run, keywords, "c:" + key));
        var chord = System.OperatingSystem.IsMacOS() ? "⌘" : "Ctrl ";
        Action("common.start_a_new_session", MessageCatalog.Get("app.action", Language), OnNewRequested, "new session chat 新建");
        Action("common.find_in_this_conversation", chord + "F", OpenFind, "find search conversation 查找 搜索 对话");
        Action("session.show_conversation", chord + "1", () => ConversationView.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)), "conversation chat flow 对话 会话");
        Action("session.show_run_analysis", chord + "2", () => AnalysisView.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)), "run analysis trace 运行 分析");
        Action("session.show_workbench", chord + "3", () => WorkbenchView.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)), "workbench browser files 工作台 浏览器");
        Action("common.files", MessageCatalog.Get("app.action", Language), () => WorkbenchView.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)), "files explorer 文件");
        Action("workbench.terminal", MessageCatalog.Get("app.action", Language), () => TerminalAction.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)), "terminal shell 终端");
        Action("common.settings", chord + ",", OpenPreferences, "settings preferences 设置 偏好");
        Action("remote.device_access", MessageCatalog.Get("app.action", Language), OpenDeviceAccess, "phone qr 手机 设备");
        Action(
            "session.wallet_and_usage",
            MessageCatalog.Get("app.action", Language),
            async () =>
            {
                OpenPreferences();
                await ShowSectionAsync("usage").ConfigureAwait(true);
            },
            "usage cost token spend 消耗 用量 费用");
        AddWorkspaceSearchDestinations(entries);
        foreach (var (target, session, project) in Projects.SearchEntries)
        {
            entries.Add(new WorkspaceSearchHit("sessions", session.Title.Length > 0 ? session.Title : session.Name, string.Join(" · ", new[] { project, target.Host, SessionAge.Readout(session.LastActiveAt, DateTimeOffset.Now, Language) }.Where(text => text.Length > 0)), () => Projects.Navigate(target), Identity: "s:" + System.Text.Json.JsonSerializer.Serialize(new[] { target.Host, target.SessionPath })));
        }

        foreach (var (section, labels) in s_destinations)
        {
            foreach (var key in labels)
            {
                entries.Add(new WorkspaceSearchHit(
                    "settings",
                    MessageCatalog.Get(key, Language),
                    section,
                    async () =>
                    {
                        OpenPreferences();
                        await ShowSectionAsync(section).ConfigureAwait(true);
                        SelectSettingsNav(section.Split(':')[0]);
                        SettingsBody.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => Equals(control.Tag, key))?.BringIntoView();
                    },
                    Identity: "g:" + section + ":" + key));
            }
        }

        _workspaceSearch.Open(entries, Language);
    }
}
