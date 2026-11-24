using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Contracts.Jobs;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Shell.Workbench;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Retained job tabs in the currently mounted workbench.</summary>
    private readonly List<BackgroundJobOutputView> _jobOutputViews = [];

    /// <summary>Opens the acknowledged job in its own workbench tab.</summary>
    /// <param name="job">The job chosen in the details panel.</param>
    private void OpenBackgroundJobOutput(BackgroundJobStatus job)
    {
        if (_engine is not { } engine)
        {
            return;
        }

        var view = _jobOutputViews.Find(tab => tab.JobId == job.Id);
        var newlyOpened = view is null;
        if (view is null)
        {
            view = new BackgroundJobOutputView(engine.Pages, job, Language)
            {
                OpeningDock = () => _workbenchDockEntrance is not null,
                StopRequested = () => ReferenceEquals(_engine, engine) ? CancelBackgroundJobsAsync([job.Id]) : Task.CompletedTask,
            };
            _jobOutputViews.Add(view);
        }

        SelectJobOutput(view, newlyOpened);
    }

    /// <summary>Places a retained job view in the workbench and starts reading while shown.</summary>
    /// <param name="view">The selected job output.</param>
    /// <param name="newlyOpened">Whether the tab itself should enter.</param>
    /// <param name="animateSelection">Whether the current selection was explicit.</param>
    private void SelectJobOutput(BackgroundJobOutputView view, bool newlyOpened = false, bool animateSelection = true)
    {
        if (ReferenceEquals(BackgroundJobHost.Content, view) && BackgroundJobHost.IsVisible && WorkbenchPanel.IsVisible && !WorkbenchStart.IsVisible)
        {
            return;
        }

        _fileRead++;
        DetachTerminal();
        HideBrowser();
        BackgroundJobHost.Content = view;
        BackgroundJobHost.IsVisible = true;
        SetWorkbenchLauncher(false, animateSelection: false);
        ExplorerPane.IsVisible = false;
        RevealWorkbench(animateSelection);
        RefreshWorkbenchLauncher();
        RefreshJobOutputVisibility();
        RebuildTabs(newlyOpened && animateSelection ? view : null, animateSelection);
    }

    /// <summary>Pauses the selected job when another view has the workbench.</summary>
    private void HideJobOutput()
    {
        BackgroundJobHost.IsVisible = false;
        RefreshJobOutputVisibility();
    }

    /// <summary>Tracks whether the selected tab is actually on the workbench canvas.</summary>
    private void RefreshJobOutputVisibility()
    {
        if (BackgroundJobHost.Content is BackgroundJobOutputView view)
        {
            view.SetShown(WorkbenchPanel.IsEffectivelyVisible && BackgroundJobHost.IsVisible && !WorkbenchStart.IsVisible);
        }
    }

    /// <summary>Refreshes open job tabs from the session snapshot used by the meter rail.</summary>
    /// <param name="status">The current conversation's status.</param>
    private void RefreshBackgroundJobOutputs(SessionStatus status)
    {
        var jobs = (status.Jobs ?? []).Concat(status.EndedJobs ?? []);
        foreach (var view in _jobOutputViews)
        {
            view.UpdateJob(jobs.FirstOrDefault(job => job.Id == view.JobId), Language);
        }
    }

    /// <summary>Appends the same tab elements and animations used by other workbench surfaces.</summary>
    /// <param name="newSurface">The view that was explicitly opened.</param>
    /// <returns>The active tab when a job owns the canvas.</returns>
    private WorkbenchHeaderTab? AppendJobOutputTabs(object? newSurface)
    {
        WorkbenchHeaderTab? active = null;
        foreach (var view in _jobOutputViews)
        {
            var selected = !WorkbenchStart.IsVisible && BackgroundJobHost.IsVisible && ReferenceEquals(view, BackgroundJobHost.Content);
            var target = new WorkbenchSurfaceTarget(view.TabTitle, () => SelectJobOutput(view), view);
            var tab = new WorkbenchHeaderTab(view.TabTitle, "Job", selected, Language) { Tag = target };
            AutomationProperties.SetAutomationId(tab.PickButton, "action:workbench.job-tab");
            AutomationProperties.SetAutomationId(tab.CloseButton, "action:workbench.close-job");
            tab.PickButton.Click += (_, _) => target.Open();
            tab.CloseButton.Click += async (_, _) =>
            {
                if (!await tab.LeaveAsync(_motionLifetime.Token).ConfigureAwait(true) || !_jobOutputViews.Remove(view))
                {
                    return;
                }

                if (ReferenceEquals(BackgroundJobHost.Content, view))
                {
                    HideJobOutput();
                    BackgroundJobHost.Content = null;
                    if (_jobOutputViews.LastOrDefault() is { } next)
                    {
                        SelectJobOutput(next);
                        return;
                    }

                    RefreshWorkbenchLauncher();
                }

                RebuildTabs();
            };
            FileTabs.Children.Add(tab);
            if (ReferenceEquals(newSurface, view) && Classes.Contains("motion"))
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
