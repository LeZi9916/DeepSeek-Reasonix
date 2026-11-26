namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Routes extension cards through the active runtime's established HTTP client.</summary>
    private void InitializeExtensionSurfaces()
    {
        Conversation.InvokeExtensionAction = name => TryAnswerAsync(async () =>
        {
            if (_engine is { } owner)
            {
                var result = await owner.Pages.InvokeExtensionActionAsync(name, _closing.Token).ConfigureAwait(true);
                if (result.Length > 0 && ReferenceEquals(_engine, owner))
                {
                    ShowNote(result);
                }
            }
        });
        Conversation.SubmitExtensionForm = (surface, values) =>
            _engine is { } owner
                ? TryAnswerAsync(() => owner.Pages.SubmitExtensionFormAsync(surface.PluginID, surface.SurfaceID, values, _closing.Token))
                : System.Threading.Tasks.Task.FromResult(false);
    }
}
