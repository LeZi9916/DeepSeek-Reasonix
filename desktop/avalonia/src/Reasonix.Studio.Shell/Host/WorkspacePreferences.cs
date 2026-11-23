using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Reasonix.Studio.Shell.Host;

/// <summary>Device-local workspace choices that do not alter kernel policy.</summary>
internal sealed record WorkspacePreferences
{
    /// <summary>Gets or initializes a value indicating whether monetary amounts are hidden.</summary>
    public bool HideAmounts
    {
        get; init;
    }

    /// <summary>Gets or initializes a value indicating whether session hover actions are shown.</summary>
    public bool SessionQuickActions
    {
        get; init;
    }

    /// <summary>Gets or initializes a value indicating whether the composer's mode picker is shown.</summary>
    public bool ShowModePicker
    {
        get; init;
    }

    /// <summary>Gets or initializes the idle tab limit, with zero preserving every pane.</summary>
    public int OpenSessionLimit
    {
        get; init;
    }

    /// <summary>Gets or initializes the explorer width, with zero using the reference width.</summary>
    public double ExplorerWidth
    {
        get; init;
    }

    /// <summary>Gets or initializes a value indicating whether hidden files are listed.</summary>
    public bool ShowHiddenFiles
    {
        get; init;
    }

    /// <summary>Gets or initializes the three most recently selected canonical palette entries.</summary>
    public IReadOnlyList<string> PaletteRecent
    {
        get; init;
    } = Array.Empty<string>();

    /// <summary>Gets or initializes the provider chips chosen in the model picker.</summary>
    public IReadOnlyList<string> ModelFilter
    {
        get; init;
    } = Array.Empty<string>();

    /// <summary>Gets or initializes the provider ordering chosen in model settings.</summary>
    public IReadOnlyList<string> ProviderOrder
    {
        get; init;
    } = Array.Empty<string>();

    /// <summary>Gets or initializes the machine names omitted from this device's workspace rail.</summary>
    public IReadOnlyList<string> HiddenRemoteHosts
    {
        get; init;
    } = Array.Empty<string>();

    /// <summary>Gets or initializes the device-local transcript disclosure defaults.</summary>
    public TranscriptFolding Folding
    {
        get; init;
    } = new();

    /// <summary>Gets or initializes a value indicating whether the collapsed plan shows progress.</summary>
    public bool ShowPlanProgress
    {
        get; init;
    } = true;

    /// <summary>Gets or initializes a value indicating whether unread answers have a divider.</summary>
    public bool ShowNewAnswerMarker
    {
        get; init;
    } = true;

    /// <summary>Gets or initializes a value indicating whether the workbench previews Office documents.</summary>
    public bool PreviewOfficeDocuments
    {
        get; init;
    } = true;

    /// <summary>Gets or initializes a value indicating whether multiple sessions are shown as tabs.</summary>
    public bool ShowSessionTabs
    {
        get; init;
    }

    /// <summary>Gets or initializes a value indicating whether one Escape stops the active turn.</summary>
    public bool SingleEscapeStop
    {
        get; init;
    }

    /// <summary>Gets or initializes a value indicating whether the composer shows its placeholder.</summary>
    public bool ShowComposerPlaceholder
    {
        get; init;
    } = true;

    /// <summary>Gets or initializes when the navigation icon column is shown.</summary>
    public string NavigationRail
    {
        get; init;
    } = "off";

    /// <summary>Gets or initializes the device-specific destination order.</summary>
    public IReadOnlyList<string> NavigationOrder
    {
        get; init;
    } = Array.Empty<string>();

    /// <summary>Gets or initializes the hidden destinations.</summary>
    public IReadOnlyList<string> HiddenNavigation
    {
        get; init;
    } = Array.Empty<string>();

    /// <summary>Gets or initializes how long the running filter keeps a completed turn.</summary>
    public int LiveGraceMinutes
    {
        get; init;
    }

    /// <summary>Gets or initializes the live event stream limit, or null for automatic.</summary>
    public int? LiveStreamLimit
    {
        get; init;
    }

    /// <summary>Gets or initializes a value indicating whether idle browser pages are allowed to sleep.</summary>
    public bool SleepIdlePages
    {
        get; init;
    }

    /// <summary>Reads optional local preferences without preventing a launch.</summary>
    /// <returns>The saved choices or defaults.</returns>
    internal static WorkspacePreferences Load()
    {
        try
        {
            return JsonSerializer.Deserialize<WorkspacePreferences>(File.ReadAllText(PathOf())) ?? new();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    /// <summary>Persists choices for the next desktop launch.</summary>
    internal void Save()
    {
        try
        {
            var path = PathOf();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Resolves the desktop-only preference file.</summary>
    /// <returns>The machine-local absolute path.</returns>
    private static string PathOf() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Reasonix", "Studio", "workspace.json");
}
