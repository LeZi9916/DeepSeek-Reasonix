using Avalonia;
using Avalonia.Controls;
using Reasonix.Studio.Kernel.Client;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>Visual state owned by one runtime, separate from its canonical workspace data.</summary>
internal sealed class WorkbenchPresentation
{
    /// <summary>Gets or initializes the project owning this visual state.</summary>
    internal string Root { get; init; } = string.Empty;

    /// <summary>Gets or initializes a value indicating whether the workbench is open.</summary>
    internal bool Open
    {
        get; init;
    }

    /// <summary>Gets or initializes a value indicating whether the workbench fills the pane.</summary>
    internal bool Expanded
    {
        get; init;
    }

    /// <summary>Gets or initializes a value indicating whether hidden files are listed.</summary>
    internal bool Hidden
    {
        get; init;
    }

    /// <summary>Gets or initializes the selected file path.</summary>
    internal string? File
    {
        get; init;
    }

    /// <summary>Gets or initializes a value indicating whether source editing is selected.</summary>
    internal bool Source
    {
        get; init;
    }

    /// <summary>Gets or initializes a value indicating whether the file diff is selected.</summary>
    internal bool Diff
    {
        get; init;
    }

    /// <summary>Gets or initializes a value indicating whether the start surface is selected.</summary>
    internal bool Launcher
    {
        get; init;
    }

    /// <summary>Gets or initializes the retained browser visual.</summary>
    internal Control? Browser
    {
        get; init;
    }

    /// <summary>Gets or initializes a value indicating whether the browser is selected.</summary>
    internal bool BrowserVisible
    {
        get; init;
    }

    /// <summary>Gets or initializes the selected kernel PTY identity.</summary>
    internal string? Terminal
    {
        get; init;
    }

    /// <summary>Gets or initializes the acknowledged directory listings.</summary>
    internal Dictionary<string, WorkspaceListing> Folders { get; init; } = [];

    /// <summary>Gets or initializes the folded explorer paths.</summary>
    internal HashSet<string> Collapsed { get; init; } = [];

    /// <summary>Gets or initializes a value indicating whether the explorer is visible.</summary>
    internal bool Explorer
    {
        get; init;
    }

    /// <summary>Gets or initializes the explorer scroll position.</summary>
    internal Vector ExplorerOffset
    {
        get; init;
    }

    /// <summary>Gets or initializes the selected Git surface.</summary>
    internal (string Title, Control View)? Git
    {
        get; init;
    }

    /// <summary>Gets or initializes the repository runtime client.</summary>
    internal StudioPageClient? GitApi
    {
        get; init;
    }

    /// <summary>Gets or sets the retained repository widget lifetime.</summary>
    internal CancellationTokenSource? GitLifetime
    {
        get; set;
    }

    /// <summary>Gets or initializes the retained repository widgets.</summary>
    internal GitRepositoryView[] Repositories { get; init; } = [];

    /// <summary>Gets or initializes the independently open browser pages.</summary>
    internal Browser.ManualWebPreview[] Browsers { get; init; } = [];

    /// <summary>Gets or initializes the browser metadata reported for this runtime.</summary>
    internal AgentBrowserTab[] AgentBrowserTabs { get; init; } = [];

    /// <summary>Gets or initializes the selected agent browser tab identity.</summary>
    internal string? AgentBrowserTarget
    {
        get; init;
    }

    /// <summary>Gets or initializes the independently open background job tabs.</summary>
    internal BackgroundJobOutputView[] Jobs { get; init; } = [];

    /// <summary>Gets or initializes the child execution records open in this conversation.</summary>
    internal SubagentRunView[] Agents { get; init; } = [];

    /// <summary>Gets or initializes the selected child execution record.</summary>
    internal SubagentRunView? SubagentRun
    {
        get; init;
    }

    /// <summary>Gets or initializes the active background job view.</summary>
    internal BackgroundJobOutputView? JobOutput
    {
        get; init;
    }
}
