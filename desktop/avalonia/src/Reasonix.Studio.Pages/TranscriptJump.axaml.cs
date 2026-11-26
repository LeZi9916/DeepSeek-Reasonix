using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Reasonix.Studio.Pages.I18n;
using System;

namespace Reasonix.Studio.Pages;

/// <summary>Offers reachable endpoints of the active transcript viewport.</summary>
internal sealed partial class TranscriptJump : UserControl
{
    /// <summary>The remaining distance needed to offer a shortcut.</summary>
    internal const double Edge = 48;

    /// <summary>Initializes a new instance of the <see cref="TranscriptJump"/> class.</summary>
    internal TranscriptJump()
    {
        InitializeComponent();
        Connect(JumpUp, down: false);
        Connect(JumpDown, down: true);
    }

    /// <summary>Requests an endpoint or the next expanded block in its direction.</summary>
    internal event Action<bool, bool>? Requested;

    /// <summary>Updates the directions offered at the current offset.</summary>
    /// <param name="above">Distance to the beginning.</param><param name="below">Distance to the end.</param>
    /// <param name="rail">Whether the message rail occupies the right edge.</param><param name="plan">Whether the plan overlays the composer edge.</param>
    /// <param name="language">Interface language.</param>
    internal void Show(double above, double below, bool rail, bool plan, InterfaceLanguage language)
    {
        Margin = new Thickness(0, 0, rail ? 45 : 14, plan ? 52 : 12);
        Show(JumpUp, above >= Edge, "chat.back_to_top", "chat.back_to_top_shift_click");
        Show(JumpDown, below >= Edge, "chat.jump_to_latest", "chat.jump_to_latest_shift_click");

        void Show(Button button, bool visible, string label, string tip)
        {
            button.IsVisible = visible;
            button.Classes.Set("shown", visible);
            AutomationProperties.SetName(button, MessageCatalog.Get(label, language));
            ToolTip.SetTip(button, MessageCatalog.Get(tip, language));
        }
    }

    /// <summary>Reads modifiers on the input that activates a jump button.</summary>
    /// <param name="button">Button to connect.</param><param name="down">Whether the button points towards the latest message.</param>
    private void Connect(Button button, bool down)
    {
        var step = false;
        button.AddHandler(PointerReleasedEvent, (_, e) => step = e.KeyModifiers.HasFlag(KeyModifiers.Shift), RoutingStrategies.Tunnel);
        button.AddHandler(KeyDownEvent, (_, e) => step = e.KeyModifiers.HasFlag(KeyModifiers.Shift), RoutingStrategies.Tunnel);
        button.Click += (_, _) =>
        {
            var requestedStep = step;
            step = false;
            Requested?.Invoke(down, requestedStep);
        };
    }
}
