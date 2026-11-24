using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Pages;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The transcript preview opened from the delegate card when viewing the in-session record.</summary>
    private Popup? _delegatePreview;

    /// <summary>Projects the adjacent process button from the same canonical rail readings.</summary>
    private void UpdateProcessChip() =>
        Processes.Show(Subagents.IsVisible || BackgroundJobs.IsVisible, Subagents.HasRunning || BackgroundJobs.Classes.Contains("live"), _engine?.Pages, Language);

    /// <summary>Stops the selected running delegate calls without stopping their parent turn.</summary>
    /// <param name="ids">The dispatches selected by the deck.</param>
    /// <returns>The kernel acknowledgements.</returns>
    private async Task StopDelegateCallsAsync(IReadOnlyList<string> ids)
    {
        var engine = _engine;
        if (engine is null)
        {
            return;
        }

        foreach (var id in ids)
        {
            try
            {
                await engine.Pages.CancelToolCallAsync(id, _closing.Token).ConfigureAwait(true);
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
    }

    /// <summary>Opens the delegate's available tool transcript from its deck row.</summary>
    /// <param name="activity">The selected dispatch.</param>
    private void OpenDelegatePreview(SubagentActivity activity)
    {
        CloseDelegatePreview();
        var call = activity.Call;
        var title = MessageCatalog.Get(
            "subagent.full_transcript_of_subagent_name",
            Language,
            ("name", string.IsNullOrEmpty(call.Call.Profile?.Name) ? "task" : call.Call.Profile.Name));
        var panel = new DockPanel();
        var close = new Button { Content = "×", Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetName(close, MessageCatalog.Get("common.close", Language));
        AutomationProperties.SetAutomationId(close, "action:agent.close");
        close.Click += (_, _) => CloseDelegatePreview();
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(14, 8, 8, 8) };
        heading.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(close, 1);
        heading.Children.Add(close);
        DockPanel.SetDock(heading, Dock.Top);
        panel.Children.Add(heading);
        if (!string.IsNullOrEmpty(call.Call.Output))
        {
            var result = new ScrollViewer
            {
                MaxHeight = ClientSize.Height * 0.3,
                Margin = new Thickness(14, 4, 14, 14),
                Content = new SelectableTextBlock { Text = call.Call.Output, TextWrapping = TextWrapping.Wrap },
            };
            DockPanel.SetDock(result, Dock.Bottom);
            panel.Children.Add(result);
        }

        if (call.Children.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = MessageCatalog.Get("subagent.no_steps_recorded_yet", Language), Margin = new Thickness(14, 6, 14, 10) });
        }
        else
        {
            var content = new SessionPage { Language = Language, Height = Math.Min(400, ClientSize.Height * 0.5) };
            content.Show(new SessionState
            {
                Items = call.Children.Select((child, index) => new ToolItem { Id = child.ID ?? FormattableString.Invariant($"step-{index}"), Call = child }).ToArray(),
            });
            panel.Children.Add(content);
        }

        var card = new Border
        {
            Width = Math.Clamp(ClientSize.Width - 56, 120, 720),
            MaxHeight = Math.Max(120, ClientSize.Height - 95),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Child = panel,
            [!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("FloatBrush"),
            [!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("BorderBrush"),
        };
        AutomationProperties.SetName(card, title);
        var popup = new Popup
        {
            PlacementTarget = LayoutRoot,
            Placement = PlacementMode.Center,
            IsLightDismissEnabled = true,
            ShouldUseOverlayLayer = true,
            Child = card,
        };
        _delegatePreview = popup;
        popup.Closed += (_, _) =>
        {
            LayoutRoot.Children.Remove(popup);
            if (ReferenceEquals(_delegatePreview, popup))
            {
                _delegatePreview = null;
                Subagents.FindControl<Button>("Trigger")?.Focus();
            }
        };
        close.KeyDown += (_, args) =>
        {
            if (args.Key == Avalonia.Input.Key.Escape)
            {
                CloseDelegatePreview();
                args.Handled = true;
            }
        };
        LayoutRoot.Children.Add(popup);
        popup.IsOpen = true;
        close.Focus();
    }

    /// <summary>Closes the delegate preview when leaving the active conversation.</summary>
    private void CloseDelegatePreview()
    {
        if (_delegatePreview is { } preview)
        {
            preview.IsOpen = false;
        }
    }
}
