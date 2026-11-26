using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Workbench;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Browser metadata acknowledged for the currently mounted workbench.</summary>
    private AgentBrowserTab[] _agentBrowserTabs = [];

    /// <summary>The latest browser-list request allowed to update the workbench.</summary>
    private long _agentBrowserRead;

    /// <summary>Gets the visible passive browser view, if one owns the workbench canvas.</summary>
    private AgentPageNote? VisibleAgentPage
    {
        get
        {
            if (RenderedFile.IsVisible && !WorkbenchStart.IsVisible && !ManualBrowserHost.IsVisible
                && !BackgroundJobHost.IsVisible && !SubagentRunHost.IsVisible && !TerminalSurface.IsVisible)
            {
                return RenderedFile.Content as AgentPageNote;
            }

            return null;
        }
    }

    /// <summary>Discards metadata belonging to a session that reused the same runtime.</summary>
    private void ResetAgentBrowserTabs()
    {
        _agentBrowserRead++;
        if (_agentBrowserTabs.Length == 0)
        {
            return;
        }

        _agentBrowserTabs = [];
        if (VisibleAgentPage is not null)
        {
            ReleasePreview();
            SetWorkbenchLauncher(true, animateSelection: false);
            return;
        }

        RebuildTabs(animateSelection: false);
    }

    /// <summary>Refreshes browser metadata from the current workbench session.</summary>
    /// <returns>The latest optional metadata read.</returns>
    private async Task RefreshAgentBrowserTabsAsync()
    {
        if (_engine is not { } engine || !WorkbenchPanel.IsVisible || _closing.IsCancellationRequested)
        {
            return;
        }

        var ticket = ++_agentBrowserRead;
        var session = _sessionPath;
        AgentBrowserTab[] tabs;
        try
        {
            var response = await engine.Pages.AgentBrowserTabsAsync(_closing.Token).ConfigureAwait(true);
            tabs = response.Where(tab => tab is { Target.Length: > 0 }).DistinctBy(tab => tab.Target).ToArray();
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            return;
        }

        if (ticket != _agentBrowserRead || !ReferenceEquals(_engine, engine) || _sessionPath != session
            || _closing.IsCancellationRequested || _agentBrowserTabs.SequenceEqual(tabs))
        {
            return;
        }

        _agentBrowserTabs = tabs;
        if (VisibleAgentPage is { } shown)
        {
            if (tabs.FirstOrDefault(tab => tab.Target == shown.Target) is { } selected)
            {
                shown.Show(selected, Language);
            }
            else
            {
                ReleasePreview();
                SetWorkbenchLauncher(true, animateSelection: false);
                return;
            }
        }

        RebuildTabs(animateSelection: false);
    }

    /// <summary>Appends passive browser tabs without providing commands to their pages.</summary>
    /// <returns>The agent tab currently selected in the workbench.</returns>
    private WorkbenchHeaderTab? AppendAgentBrowserTabs()
    {
        WorkbenchHeaderTab? active = null;
        var shown = VisibleAgentPage;
        foreach (var tab in _agentBrowserTabs)
        {
            var title = AgentPageNote.Label(tab, Language);
            var selected = shown?.Target == tab.Target;
            var note = selected ? shown! : new AgentPageNote();
            var target = new WorkbenchSurfaceTarget(title, () => SelectAgentBrowserTab(tab.Target, note), note);
            var header = new WorkbenchHeaderTab(title, "Browser", selected, Language) { Tag = target };
            header.Classes.Set("agent-page-active", tab.Active);
            header.CloseButton.IsVisible = false;
            AutomationProperties.SetAutomationId(header.PickButton, "action:workbench.agent-browser-tab");
            var tooltip = tab.Active
                ? title + " · " + MessageCatalog.Get("workbench.the_model_is_working_on", Language)
                : title;
            AutomationProperties.SetName(header.PickButton, tooltip);
            ToolTip.SetTip(header.PickButton, tooltip);
            header.PickButton.Click += (_, _) => target.Open();
            FileTabs.Children.Add(header);
            if (selected)
            {
                active = header;
            }
        }

        return active;
    }

    /// <summary>Projects a selected browser identity into an inert page note.</summary>
    /// <param name="target">The current tab identity.</param><param name="note">An optional retained note for the tab.</param>
    /// <param name="animateSelection">Whether this selection was requested directly.</param>
    private void SelectAgentBrowserTab(string target, AgentPageNote? note = null, bool animateSelection = true)
    {
        if (_agentBrowserTabs.FirstOrDefault(tab => tab.Target == target) is not { } tab)
        {
            return;
        }

        _fileRead++;
        HideBrowser();
        SetWorkbenchLauncher(false, animateSelection);
        note ??= new AgentPageNote();
        note.Show(tab, Language);
        ShowPreview(note);
        FileCanvas.RowDefinitions[0].Height = new Avalonia.Controls.GridLength(40);
        FileModes.IsVisible = SaveFile.IsVisible = FileMore.IsVisible = false;
        SetReadOnlyBadge(null);
        SetFileBarTitle(AgentPageNote.Label(tab, Language), path: false);
        RefreshWorkbenchLauncher();
        RebuildTabs(animateSelection: animateSelection);
    }
}
