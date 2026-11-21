namespace Reasonix.Studio.Kernel.Client;

/// <summary>Kernel preferences that expose a boolean control in the native settings sheet.</summary>
public enum StudioSetting
{
    /// <summary>The <c>plan</c> setting at <c>/default-mode</c>.</summary>
    DefaultPlan,

    /// <summary>The <c>fold</c> setting at <c>/fold-work-settings</c>.</summary>
    FoldWork,

    /// <summary>The <c>enabled</c> setting at <c>/present-files</c>.</summary>
    PresentFiles,

    /// <summary>The <c>show</c> setting at <c>/changed-files-settings</c>.</summary>
    ChangedFiles,

    /// <summary>The <c>enabled</c> setting at <c>/providers/health/settings</c>.</summary>
    ProviderHealth,

    /// <summary>The <c>enabled</c> setting at <c>/browser-tools</c>.</summary>
    BrowserTools,

    /// <summary>The <c>enabled</c> setting at <c>/auto-archive</c>.</summary>
    AutoArchive,

    /// <summary>The <c>enabled</c> setting at <c>/observability</c>.</summary>
    TraceExport,
}
