using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Composer;

/// <summary>The process reading next to the subagent and background-job cards.</summary>
internal sealed partial class ProcessChip : UserControl
{
    /// <summary>The active conversation's API.</summary>
    private StudioPageClient? _api;

    /// <summary>The current interface language.</summary>
    private InterfaceLanguage _language;

    /// <summary>The read for the current opening.</summary>
    private CancellationTokenSource? _reading;

    /// <summary>Initializes a new instance of the <see cref="ProcessChip"/> class.</summary>
    public ProcessChip()
    {
        InitializeComponent();
        Details.CanHover = () =>
        {
            if (!Details.IsOpen)
            {
                _ = ReadAsync();
            }

            return true;
        };
        Details.Pinned += () => _ = ReadAsync();
        DetachedFromVisualTree += (_, _) => Close();
    }

    /// <summary>Updates the visibility and lifecycle colour from the deck readings.</summary>
    /// <param name="available">Whether the rail has subagents or background jobs.</param>
    /// <param name="live">Whether any are still running.</param>
    /// <param name="api">The conversation that owns the process list.</param>
    /// <param name="language">The reader's language.</param>
    internal void Show(bool available, bool live, StudioPageClient? api, InterfaceLanguage language)
    {
        if (!ReferenceEquals(_api, api) || !available)
        {
            Close();
        }

        _api = api;
        _language = language;
        IsVisible = available;
        Classes.Set("live", live);
        Caption.Text = MessageCatalog.Get("subagent.process.chip", language);
        AutomationProperties.SetName(Trigger, Caption.Text);
    }

    /// <summary>Dismisses this conversation's process details.</summary>
    internal void Close()
    {
        _reading?.Cancel();
        Details.Close(immediately: true);
    }

    /// <summary>Reads the canonical process list when the reader asks to see it.</summary>
    /// <returns>The current process read.</returns>
    private async Task ReadAsync()
    {
        if (_api is not { } api || _reading is { IsCancellationRequested: false })
        {
            return;
        }

        using var read = new CancellationTokenSource();
        _reading = read;
        try
        {
            var result = await api.ReadProcessesAsync(read.Token).ConfigureAwait(true);
            if (!read.IsCancellationRequested)
            {
                Rows.Children.Clear();
                foreach (var process in result.OfType<JsonObject>())
                {
                    var kind = Localize("kind", (string?)process["kind"]);
                    var state = Localize("state", (string?)process["state"]);
                    var cause = Localize("cause", (string?)process["cause"]);
                    var started = (long?)process["startedAt"] ?? 0;
                    var time = started > 0 ? Format.JobElapsed(Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - started), _language) : string.Empty;
                    Rows.Children.Add(new TextBlock
                    {
                        Text = string.Join(" · ", new[] { (string?)process["name"], kind, state, time }.Where(term => !string.IsNullOrEmpty(term))),
                        TextWrapping = TextWrapping.Wrap,
                    });
                    if (cause.Length > 0)
                    {
                        Rows.Children.Add(new TextBlock { Text = cause, [!TextBlock.ForegroundProperty] = StudioTypography.Resource("FaintBrush") });
                    }
                }

                if (Rows.Children.Count == 0)
                {
                    Rows.Children.Add(new TextBlock { Text = MessageCatalog.Get("subagent.process.empty", _language) });
                }
            }
        }
        catch (OperationCanceledException) when (read.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!read.IsCancellationRequested)
            {
                Rows.Children.Clear();
                Rows.Children.Add(new TextBlock { Text = error.Message, TextWrapping = TextWrapping.Wrap });
            }
        }
        finally
        {
            if (ReferenceEquals(_reading, read))
            {
                _reading = null;
            }
        }

        string Localize(string category, string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var key = $"subagent.process.{category}.{value}";
            var label = MessageCatalog.Get(key, _language);
            return label == key ? value : label;
        }
    }
}
