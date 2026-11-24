using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Settings;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The current service or model reveal.</summary>
    private CancellationTokenSource? _setupChoicesMotion;

    /// <summary>Builds a radio row for a discovered model.</summary>
    /// <param name="name">The provider model identity.</param>
    /// <param name="selected">Whether the model is currently selected.</param>
    /// <returns>The selectable model row.</returns>
    private static RadioButton CreateModelChoice(string name, bool selected)
    {
        var label = new TextBlock { Text = name, Classes = { "setup-choice-name" } };
        var detail = new TextBlock { Classes = { "setup-choice-detail" } };
        Grid.SetColumn(detail, 1);
        return new RadioButton
        {
            Tag = name,
            RenderTransform = new TranslateTransform(),
            Classes = { "setup-choice", "setup-model" },
            IsChecked = selected,
            Content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10, Children = { label, detail } },
        };
    }

    /// <summary>Builds a provider tile from its existing localized caption and vendor mark.</summary>
    /// <param name="label">Name or translated name key.</param>
    /// <param name="hint">Translated subtitle key.</param>
    /// <param name="mark">The existing vendor artwork identity.</param>
    /// <returns>The selectable service tile.</returns>
    private RadioButton CreateServiceChoice(string label, string hint, string? mark)
    {
        var heading = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        if (ProviderVendorMark.Create(this, mark, 16) is { } icon)
        {
            heading.Children.Add(icon);
        }

        heading.Children.Add(new TextBlock { Classes = { "setup-choice-name" } });
        var radio = new RadioButton
        {
            Tag = (label, hint),
            RenderTransform = new TranslateTransform(),
            Classes = { "setup-choice", "setup-service" },
            Content = new StackPanel
            {
                Spacing = 2,
                Children = { heading, new TextBlock { Classes = { "setup-choice-detail" } } },
            },
        };
        return radio;
    }

    /// <summary>Refreshes the heading, subtitle and accessible name.</summary>
    /// <param name="radio">The service tile.</param>
    private void LabelServiceChoice(RadioButton radio)
    {
        var (label, hint) = ((string Label, string Hint))radio.Tag!;
        var name = label.StartsWith("onboarding.", StringComparison.Ordinal) ? MessageCatalog.Get(label, Language) : label;
        var content = (StackPanel)radio.Content!;
        ((TextBlock)((StackPanel)content.Children[0]).Children[^1]).Text = name;
        ((TextBlock)content.Children[1]).Text = MessageCatalog.Get(hint, Language);
        AutomationProperties.SetName(radio, name);
    }

    /// <summary>Displays the currently selected shortcut's preset endpoint.</summary>
    private void LabelServiceNote()
    {
        if (SetupServiceNote.IsVisible &&
            Uri.TryCreate(ServiceAddress.Text, UriKind.Absolute, out var address) &&
            SetupServices.Children.OfType<RadioButton>().FirstOrDefault(radio => radio.IsChecked == true) is { } selected)
        {
            var (label, _) = ((string Label, string Hint))selected.Tag!;
            var name = label.StartsWith("onboarding.", StringComparison.Ordinal) ? MessageCatalog.Get(label, Language) : label;
            SetupServiceNote.Text = MessageCatalog.Get(
                MessageKey.Onboarding.AddressFilledForName, Language, ("name", name), ("host", address.Host));
        }
    }

    /// <summary>Shows the default and image capability descriptions when supplied by the provider.</summary>
    /// <param name="radio">The model row.</param>
    /// <param name="probe">The discovery result.</param>
    private void LabelModelChoice(RadioButton radio, ProviderProbe probe)
    {
        var name = (string)radio.Tag!;
        var detail = (TextBlock)((Grid)radio.Content!).Children[1];
        detail.Text = string.Join(" · ", new[]
        {
            name == probe.Default ? MessageCatalog.Get(MessageKey.Common.Default, Language) : string.Empty,
            probe.Vision?.Contains(name, StringComparer.Ordinal) == true ? MessageCatalog.Get(MessageKey.Common.ReadsImages, Language) : string.Empty,
        }.Where(text => text.Length > 0));
        detail.IsVisible = detail.Text.Length > 0;
        AutomationProperties.SetName(radio, name);
    }

    /// <summary>Reveals the choices when the user advances to or returns to their step.</summary>
    /// <param name="panel">The active stage's choices.</param>
    private async void EnterSetupChoices(Panel panel)
    {
        _setupChoicesMotion?.Cancel();
        if (!Classes.Contains("motion") || !panel.IsEffectivelyVisible || panel.Children.Count == 0)
        {
            return;
        }

        using var entrance = CancellationTokenSource.CreateLinkedTokenSource(_motionLifetime.Token);
        _setupChoicesMotion = entrance;
        try
        {
            await Task.WhenAll(panel.Children.Select((choice, index) => new Animation
            {
                Delay = TimeSpan.FromMilliseconds(36 * Math.Min(index, 7)),
                Duration = TimeSpan.FromMilliseconds(180),
                Easing = new SplineEasing(0.16, 1, 0.3, 1),
                FillMode = FillMode.Backward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, 6d) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) } },
                },
            }.RunAsync(choice, entrance.Token))).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (entrance.IsCancellationRequested)
        {
        }
        finally
        {
            if (_setupChoicesMotion == entrance)
            {
                _setupChoicesMotion = null;
            }
        }
    }
}
