using Avalonia.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>The signed-in publisher's packages and their own install preview.</summary>
internal sealed partial class MarketMineView : SettingsFormView
{
    /// <summary>Initializes a new instance of the <see cref="MarketMineView"/> class.</summary>
    public MarketMineView() => InitializeComponent();

    /// <summary>Draws the package inventory from the current account.</summary>
    /// <param name="api">Owning runtime.</param>
    /// <param name="language">Settings language.</param>
    /// <param name="describe">Refusal formatter.</param>
    /// <param name="newVersion">Opens the publish draft for a previous package.</param>
    /// <param name="showInstalled">Opens installed capabilities selected after installation.</param>
    /// <param name="applyingChanged">Locks navigation during writes.</param>
    /// <param name="token">Window lifetime.</param>
    /// <returns>The first read.</returns>
    internal async Task OpenAsync(
        StudioPageClient api,
        InterfaceLanguage language,
        Func<Exception, string> describe,
        Action<JsonObject> newVersion,
        Func<string, string, Task>? showInstalled,
        Action<bool> applyingChanged,
        CancellationToken token)
    {
        Translate(language);
        var busy = false;
        Retry.Click += async (_, _) => await ReadAsync().ConfigureAwait(true);
        await ReadAsync().ConfigureAwait(true);

        async Task ReadAsync()
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            MineRows.Children.Clear();
            MineError.IsVisible = false;
            MineNoteFrame.IsVisible = true;
            MineNotice.Text = Text("common.loading");
            try
            {
                var response = await api.MarketMineAsync(token).ConfigureAwait(true);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                foreach (var package in response["packages"]?.AsArray().OfType<JsonObject>() ?? [])
                {
                    var row = new MarketMineRow();
                    row.Open(package, language, newVersion, InspectAsync, SubmitAsync);
                    MineRows.Children.Add(row);
                }

                MineNoteFrame.IsVisible = MineRows.Children.Count == 0;
                MineNotice.Text = Text("ext.nothing_published_yet");
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    MineNoteFrame.IsVisible = false;
                    MineError.IsVisible = true;
                    MineFailure.Text = describe(error);
                }
            }
        }

        async Task InspectAsync(JsonObject package)
        {
            if (busy || PreviewHost.IsVisible)
            {
                return;
            }

            var preview = new MarketInstall();
            PreviewHost.Content = preview;
            PreviewHost.IsVisible = true;
            Inventory.IsVisible = false;
            await preview.OpenOwnAsync(
                api,
                package,
                language,
                describe,
                async () =>
                {
                    PreviewHost.Content = null;
                    PreviewHost.IsVisible = false;
                    Inventory.IsVisible = true;
                    await ReadAsync().ConfigureAwait(true);
                },
                applyingChanged,
                token,
                showInstalled).ConfigureAwait(true);
        }

        async Task<string?> SubmitAsync(string slug)
        {
            if (busy)
            {
                return null;
            }

            busy = true;
            applyingChanged(true);
            try
            {
                await api.SubmitMarketAsync(slug, token).ConfigureAwait(true);
                await ReadAsync().ConfigureAwait(true);
                return null;
            }
            catch (Exception error)
            {
                return token.IsCancellationRequested ? null : describe(error);
            }
            finally
            {
                applyingChanged(false);
                busy = false;
            }
        }
    }
}
