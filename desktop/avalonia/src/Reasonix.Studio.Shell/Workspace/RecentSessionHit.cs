using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using System.ComponentModel;

namespace Reasonix.Studio.Shell.Workspace;

/// <summary>A recent history row keeps its canonical machine and working directory.</summary>
/// <param name="Target">Canonical navigation identity.</param><param name="Session">Acknowledged session.</param>
/// <param name="Project">Canonical project name.</param><param name="Age">The local-time reading.</param><param name="Group">Optional calendar section.</param>
public sealed record RecentSessionHit(SessionOpenTarget Target, TreeSession Session, string Project, string Age, string Group = "") : INotifyPropertyChanged
{
    /// <summary>The canonical status projected without replacing the native row.</summary>
    private string _tail = Age;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the canonical display title.</summary>
    public string Title => Session.Title.Length > 0 ? Session.Title : Session.Name;

    /// <summary>Gets the project and actual working directory.</summary>
    public string Detail => Project + " · " + Target.WorkspaceRoot;

    /// <summary>Gets a value indicating whether this row starts a calendar section.</summary>
    public bool HasGroup => Group.Length > 0;

    /// <summary>Gets the acknowledged run label or saved activity age.</summary>
    public string Tail => _tail;

    /// <summary>Gets the reference's age or live-status type size.</summary>
    public double TailSize { get; private set; } = 11;

    /// <summary>Projects live reducer state while retaining the row's focus and click target.</summary>
    /// <param name="run">Running, halt, or empty.</param><param name="language">Interface language.</param>
    public void ShowRun(string run, InterfaceLanguage language)
    {
        var tail = run is "running" or "halt" ? MessageCatalog.Get(run == "running" ? "common.running" : "session.needs_you", language) : Age;
        if (tail != _tail)
        {
            _tail = tail;
            TailSize = run is "running" or "halt" ? 13 : 11;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Tail)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TailSize)));
        }
    }
}
