using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

internal sealed partial class InstalledExtensionsView
{
    /// <summary>Returns the successful action to its ordinary label after it has been read.</summary>
    private readonly DispatcherTimer _reloadSettled = new() { Interval = TimeSpan.FromSeconds(4) };

    /// <summary>Identifies the feedback owned by the latest reload or extension write.</summary>
    private int _reloadRevision;

    /// <summary>Whether the active reload request is outstanding.</summary>
    private bool _reloading;

    /// <summary>The phases visible on the runtime reload control.</summary>
    private enum ReloadFeedback
    {
        /// <summary>The action is available.</summary>
        Offer,

        /// <summary>The runtime is reloading.</summary>
        Running,

        /// <summary>The runtime acknowledged the reload.</summary>
        Applied,

        /// <summary>The runtime reported a refusal.</summary>
        Refused,
    }

    /// <summary>Connects the control to the active runtime rather than to an inventory refresh.</summary>
    private void InitializeReload()
    {
        Reload.Click += async (_, _) => await ReloadRuntimeAsync().ConfigureAwait(true);
        _reloadSettled.Tick += (_, _) => SetReloadFeedback(ReloadFeedback.Offer);
        DetachedFromVisualTree += (_, _) =>
        {
            _reloadRevision++;
            SetReloadFeedback(ReloadFeedback.Offer);
        };
    }

    /// <summary>Requests an explicit runtime reload and shows the kernel's acknowledgement or refusal.</summary>
    /// <returns>The reload and successful inventory readback.</returns>
    private async Task ReloadRuntimeAsync()
    {
        if (_api is not { } api || _token.IsCancellationRequested || _reloading)
        {
            return;
        }

        var revision = ++_reloadRevision;
        var token = _token;
        SetReloadFeedback(ReloadFeedback.Running, Text("app.restarting_resident_processes_and_rescanning"));
        try
        {
            await api.ReloadExtensionsAsync(token).ConfigureAwait(true);
        }
        catch (Exception error)
        {
            if (revision == _reloadRevision && !token.IsCancellationRequested)
            {
                SetReloadFeedback(ReloadFeedback.Refused, _describe!(error));
            }

            return;
        }

        if (revision != _reloadRevision || token.IsCancellationRequested)
        {
            return;
        }

        SetReloadFeedback(ReloadFeedback.Applied, Text("app.live_the_next_turn_runs"));
        try
        {
            await Task.WhenAll(LoadPackagesAsync(), ChangeScopeAsync(_root)).ConfigureAwait(true);
        }
        catch (Exception error)
        {
            if (revision == _reloadRevision && !token.IsCancellationRequested)
            {
                Report(error);
            }
        }
    }

    /// <summary>Reports the reload outcome of an already persisted package change.</summary>
    /// <param name="failure">The host's reload refusal, when the write still needs a reload.</param>
    private void ShowReloadFailure(string? failure)
    {
        _reloadRevision++;
        if (string.IsNullOrEmpty(failure))
        {
            SetReloadFeedback(ReloadFeedback.Applied, Text("app.live_the_next_turn_runs"));
        }
        else
        {
            SetReloadFeedback(ReloadFeedback.Refused, Text("app.changes_saved_but_the_runtime", ("reason", failure)));
        }
    }

    /// <summary>Projects the reload phase independently from other inventory errors.</summary>
    /// <param name="feedback">The accepted phase of the reload.</param>
    /// <param name="note">The phase's reader-facing detail.</param>
    private void SetReloadFeedback(ReloadFeedback feedback, string? note = null)
    {
        _reloadSettled.Stop();
        _reloading = feedback == ReloadFeedback.Running;
        Reload.Classes.Set("reloading", _reloading);
        Reload.Classes.Set("applied", feedback == ReloadFeedback.Applied);
        Reload.Classes.Set("refused", feedback == ReloadFeedback.Refused);
        Reload.IsEnabled = !_reloading;
        ReloadLabel.Text = Text(feedback switch
        {
            ReloadFeedback.Running => "app.reloading",
            ReloadFeedback.Applied => "app.live",
            _ => "app.reload_runtime",
        });
        ReloadNote.Classes.Set("applied", feedback == ReloadFeedback.Applied);
        ReloadNote.Classes.Set("refused", feedback == ReloadFeedback.Refused);
        ReloadNote.Text = note;
        ReloadNote.IsVisible = !string.IsNullOrEmpty(note);
        ReloadNote.Classes.Set("present", ReloadNote.IsVisible);
        if (feedback == ReloadFeedback.Applied)
        {
            _reloadSettled.Start();
        }
    }
}
