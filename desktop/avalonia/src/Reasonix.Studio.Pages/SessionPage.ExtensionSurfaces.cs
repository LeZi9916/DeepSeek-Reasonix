using Avalonia.Controls;
using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.State.Transcript;
using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Pages;

public sealed partial class SessionPage
{
    /// <summary>Gets or sets the window's extension action acknowledgement path.</summary>
    public Func<string, Task<bool>>? InvokeExtensionAction
    {
        get;
        set;
    }

    /// <summary>Gets or sets the window's extension form acknowledgement path.</summary>
    public Func<ExtensionSurface, JsonObject, Task<bool>>? SubmitExtensionForm
    {
        get;
        set;
    }

    /// <summary>Draws the extension's current surface and reconnects its retained form.</summary>
    /// <param name="item">The canonical extension row.</param>
    /// <returns>The native surface presentation.</returns>
    private Control ExtensionBody(ExtensionItem item)
    {
        var surface = item.Surface;
        var drafts = DisclosureChoices.ExtensionDrafts;
        var key = (surface.PluginID, surface.SurfaceID, surface.Generation);
        if (!drafts.TryGetValue(key, out var draft))
        {
            draft = new ExtensionSurfaceDraft();
            if (surface.Form is not null)
            {
                drafts[key] = draft;
            }
        }

        return new ExtensionSurfaceView(surface, draft, Language, InvokeExtensionAction, SubmitExtensionForm);
    }
}
