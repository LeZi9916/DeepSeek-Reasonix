using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Shell.Workbench;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Job-independent subagent records open in the current workbench.</summary>
    private readonly List<SubagentRunView> _subagentRuns = [];

    /// <summary>The lookup owned by the most recently clicked delegation row.</summary>
    private CancellationTokenSource? _delegateLookup;

    /// <summary>Resolves a clicked delegation to the kernel's matching runs.</summary>
    /// <param name="activity">The clicked deck row.</param>
    /// <returns>The lookup and opening of matching workbench tabs.</returns>
    private async Task OpenDelegateRunAsync(SubagentActivity activity)
    {
        _delegateLookup?.Cancel();
        var call = activity.Call.Call.ID;
        if (_engine is not { } engine || string.IsNullOrEmpty(call))
        {
            OpenDelegatePreview(activity);
            return;
        }

        using var lookup = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        _delegateLookup = lookup;
        var session = _composerStatus.SessionPath;
        SubagentRun[] runs;
        try
        {
            runs = await engine.Pages.ReadSubagentRunsAsync(lookup.Token).ConfigureAwait(true);
        }
        catch (Exception)
        {
            if (lookup.IsCancellationRequested)
            {
                return;
            }

            runs = [];
        }
        finally
        {
            if (ReferenceEquals(_delegateLookup, lookup))
            {
                _delegateLookup = null;
            }
        }

        if (lookup.IsCancellationRequested || !ReferenceEquals(_engine, engine) || _composerStatus.SessionPath != session)
        {
            return;
        }

        var matched = runs.Where(run => run.ParentCall == call && !string.IsNullOrEmpty(run.Ref)).ToArray();
        if (matched.Length == 0)
        {
            OpenDelegatePreview(activity);
            return;
        }

        CloseDelegatePreview();
        var added = new HashSet<SubagentRunView>();
        foreach (var run in matched)
        {
            if (_subagentRuns.Any(open => open.RunId == run.Ref))
            {
                continue;
            }

            var view = new SubagentRunView(engine.Pages, run, Language)
            {
                StopRequested = id => ReferenceEquals(_engine, engine) ? StopDelegateCallsAsync([id]) : Task.CompletedTask,
            };
            view.ReturnToCallRequested += id =>
            {
                if (_fullWorkbench)
                {
                    _fullWorkbench = false;
                    LayoutWorkbench();
                }

                if (ConversationArea.IsEffectivelyVisible)
                {
                    Conversation.RevealToolCall(id);
                }
                else
                {
                    HideWorkbenchAfterDismissal();
                    Conversation.RevealToolCall(id);
                }
            };
            _subagentRuns.Add(view);
            added.Add(view);
        }

        SelectSubagentRun(_subagentRuns.First(view => view.RunId == matched[0].Ref), added);
    }

    /// <summary>Selects a retained record in the same workbench used by files and jobs.</summary>
    /// <param name="view">The requested run.</param>
    /// <param name="newViews">New tabs opened on this interaction.</param>
    /// <param name="animateSelection">Whether the reader explicitly changed the canvas.</param>
    private void SelectSubagentRun(SubagentRunView view, IReadOnlySet<SubagentRunView>? newViews = null, bool animateSelection = true)
    {
        if (ReferenceEquals(SubagentRunHost.Content, view) && SubagentRunHost.IsVisible && WorkbenchPanel.IsVisible && !WorkbenchStart.IsVisible)
        {
            if (newViews is { Count: > 0 })
            {
                RebuildTabs(animateSelection ? newViews : null, animateSelection);
            }

            return;
        }

        _fileRead++;
        DetachTerminal();
        HideBrowser();
        SubagentRunHost.Content = view;
        SubagentRunHost.IsVisible = true;
        SetWorkbenchLauncher(false, animateSelection: false);
        ExplorerPane.IsVisible = false;
        RevealWorkbench(animateSelection);
        RefreshWorkbenchLauncher();
        RefreshSubagentRunVisibility();
        RebuildTabs(animateSelection ? newViews : null, animateSelection);
    }

    /// <summary>Pauses the active run while another surface owns the workbench.</summary>
    private void HideSubagentRun()
    {
        SubagentRunHost.IsVisible = false;
        RefreshSubagentRunVisibility();
    }

    /// <summary>Starts transcript reads only when the run is actually visible.</summary>
    private void RefreshSubagentRunVisibility()
    {
        if (SubagentRunHost.Content is SubagentRunView view)
        {
            view.SetShown(WorkbenchPanel.IsEffectivelyVisible && SubagentRunHost.IsVisible && !WorkbenchStart.IsVisible);
        }
    }

    /// <summary>Appends workbench tabs for the runs opened in this conversation.</summary>
    /// <param name="newSurface">The newly opened tabs, if any.</param>
    /// <returns>The active run tab.</returns>
    private WorkbenchHeaderTab? AppendSubagentRunTabs(object? newSurface)
    {
        WorkbenchHeaderTab? active = null;
        foreach (var view in _subagentRuns)
        {
            var selected = !WorkbenchStart.IsVisible && SubagentRunHost.IsVisible && ReferenceEquals(view, SubagentRunHost.Content);
            var target = new WorkbenchSurfaceTarget(view.TabTitle, () => SelectSubagentRun(view), view);
            var tab = new WorkbenchHeaderTab(view.TabTitle, "Agent", selected, Language) { Tag = target };
            AutomationProperties.SetAutomationId(tab.PickButton, "action:workbench.agent-tab");
            AutomationProperties.SetAutomationId(tab.CloseButton, "action:workbench.close-agent");
            tab.PickButton.Click += (_, _) => target.Open();
            tab.CloseButton.Click += async (_, _) =>
            {
                if (!await tab.LeaveAsync(_motionLifetime.Token).ConfigureAwait(true) || !_subagentRuns.Remove(view))
                {
                    return;
                }

                if (ReferenceEquals(SubagentRunHost.Content, view))
                {
                    var visible = SubagentRunHost.IsVisible && !WorkbenchStart.IsVisible;
                    HideSubagentRun();
                    SubagentRunHost.Content = null;
                    if (visible && _subagentRuns.LastOrDefault() is { } next)
                    {
                        SelectSubagentRun(next);
                        return;
                    }

                    RefreshWorkbenchLauncher();
                }

                RebuildTabs();
            };
            FileTabs.Children.Add(tab);
            if (newSurface is IReadOnlySet<SubagentRunView> added && added.Contains(view) && Classes.Contains("motion"))
            {
                tab.Enter(_motionLifetime.Token);
            }

            if (selected)
            {
                active = tab;
            }
        }

        return active;
    }
}
