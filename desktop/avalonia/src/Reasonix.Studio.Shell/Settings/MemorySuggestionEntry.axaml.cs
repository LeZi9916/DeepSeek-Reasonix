using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>An explicit review decision for one held memory proposal.</summary>
public sealed partial class MemorySuggestionEntry : SettingsFormView
{
    /// <summary>Initializes a new instance of the <see cref="MemorySuggestionEntry"/> class.</summary>
    public MemorySuggestionEntry() => InitializeComponent();

    /// <summary>Binds one proposal to its originating runtime.</summary>
    /// <param name="proposal">The stored suggestion.</param>
    /// <param name="language">Shell language.</param>
    /// <param name="api">Owning runtime.</param>
    /// <param name="remove">Removes the accepted or dismissed card.</param>
    /// <param name="fail">Displays a refusal.</param>
    /// <param name="token">Window lifetime.</param>
    internal void Open(JsonObject proposal, InterfaceLanguage language, StudioPageClient api, Action remove, Action<Exception> fail, CancellationToken token)
    {
        Translate(language);
        var id = (string)proposal["id"]!;
        Tag = id;
        Title.Text = (string?)proposal["title"] ?? (string?)proposal["description"];
        Scope.Text = Text((string?)proposal["scope"] == "global" ? "chat.remember_scope_global" : "chat.remember_scope_project");
        Edit.Click += (_, _) =>
        {
            Body.Text = (string?)proposal["body"] ?? string.Empty;
            Editor.IsVisible = true;
            Actions.IsVisible = false;
            Body.Focus();
        };
        Cancel.Click += (_, _) =>
        {
            Editor.IsVisible = false;
            Actions.IsVisible = true;
            Edit.Focus();
        };
        Save.Click += async (_, _) => await SettleAsync(true, Body.Text ?? string.Empty).ConfigureAwait(true);
        Accept.Click += async (_, _) => await SettleAsync(true, null).ConfigureAwait(true);
        Dismiss.Click += async (_, _) => await SettleAsync(false, null).ConfigureAwait(true);

        async Task SettleAsync(bool accept, string? body)
        {
            if (!IsEnabled)
            {
                return;
            }

            IsEnabled = false;
            try
            {
                await api.SettleMemoryAsync(id, accept, body, token).ConfigureAwait(true);
                remove();
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    fail(error);
                }
            }
            finally
            {
                IsEnabled = true;
            }
        }
    }
}
