using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Requests stops within the pane that owns the selected jobs.</summary>
    /// <param name="ids">The job identifiers confirmed by the reader.</param>
    /// <returns>The stop requests and a fresh status snapshot.</returns>
    private async Task CancelBackgroundJobsAsync(IReadOnlyList<string> ids)
    {
        var engine = _engine;
        var pane = _activePane;
        var session = _composerStatus.SessionPath;
        if (engine is null)
        {
            return;
        }

        foreach (var id in ids)
        {
            try
            {
                await engine.Pages.CancelBackgroundJobAsync(id, _closing.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (_closing.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                if (ReferenceEquals(_engine, engine))
                {
                    ShowRefusal(error);
                }
            }
        }

        if (!ReferenceEquals(_engine, engine) || _composerStatus.SessionPath != session)
        {
            return;
        }

        if (pane is not null && ReferenceEquals(pane.Engine, engine) && ReferenceEquals(_activePane, pane))
        {
            await ReadPaneStatusAsync(pane).ConfigureAwait(true);
        }
        else if (pane is null)
        {
            try
            {
                var status = await engine.StatusAsync(_closing.Token).ConfigureAwait(true);
                if (status is not null && ReferenceEquals(_engine, engine) && _composerStatus.SessionPath == session && status.SessionPath == session)
                {
                    ShowComposeStatus(status);
                }
            }
            catch (OperationCanceledException) when (_closing.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (ReferenceEquals(_engine, engine))
                {
                    ShowRefusal(error);
                }
            }
        }
    }
}
