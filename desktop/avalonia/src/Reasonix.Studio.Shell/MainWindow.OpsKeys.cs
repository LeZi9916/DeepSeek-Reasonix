using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The duration during which a second Escape stops the turn.</summary>
    private readonly DispatcherTimer _stopArm = new() { Interval = TimeSpan.FromMilliseconds(2500) };

    /// <summary>Prepares the first-Escape hint and clears it on close.</summary>
    private void InitializeEscapeStop()
    {
        _stopArm.Tick += (_, _) => DisarmStop();
        Closed += (_, _) => _stopArm.Stop();
    }

    /// <summary>Clears the first-Escape hint.</summary>
    private void DisarmStop()
    {
        _stopArm.Stop();
        StopHint.IsVisible = false;
    }

    /// <summary>Arms or cancels the active turn after other transient controls consume Escape.</summary>
    private void EscapeStop()
    {
        if (_workspacePreferences.SingleEscapeStop || _stopArm.IsEnabled)
        {
            DisarmStop();
            Stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        else
        {
            StopHintText.Text = MessageCatalog.Get("composer.press_esc_again_to_stop", Language);
            StopHint.IsVisible = true;
            _stopArm.Start();
        }
    }

    /// <summary>Addresses the next or preceding user message in the current transcript.</summary>
    /// <param name="next">Whether to search toward the end of the conversation.</param>
    private void JumpToQuestion(bool next) => Conversation.MoveBetweenUserMessages(next);

    /// <summary>Changes zoom within the same bounds applied by its settings row.</summary>
    /// <param name="step">Direction of the zoom, or zero to restore it.</param>
    /// <returns>The canonical appearance readback.</returns>
    private async Task StepZoomAsync(int step)
    {
        if (_engine?.Pages is not { } pages)
        {
            return;
        }

        try
        {
            var current = (double?)_look["zoom"] ?? 1;
            var min = (double?)_look["zoomRange"]?["min"] ?? 0.75;
            var max = (double?)_look["zoomRange"]?["max"] ?? 1.75;
            var value = step == 0 ? 1 : Math.Clamp(Math.Round((current + (step * 0.05)) * 20) / 20, min, max);
            if (value != current)
            {
                var applied = await pages.SetAppearanceAsync(null, value, _closing.Token).ConfigureAwait(true);
                ApplyAppearance(applied, Conversation.FoldsTurnWork);
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            ShowRefusal(error);
        }
    }
}
