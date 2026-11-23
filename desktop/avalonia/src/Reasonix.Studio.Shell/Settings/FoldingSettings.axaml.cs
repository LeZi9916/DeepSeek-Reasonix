using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Edits the local transcript disclosure defaults and previews their effects.</summary>
public sealed partial class FoldingSettings : SettingsFormView
{
    /// <summary>Initializes a new instance of the <see cref="FoldingSettings"/> class.</summary>
    public FoldingSettings() => InitializeComponent();

    /// <summary>Connects the settings to the window's transcript preferences.</summary>
    /// <param name="language">Settings language.</param><param name="folding">Current modes.</param>
    /// <param name="changed">Applies edits to the window.</param>
    internal void Open(InterfaceLanguage language, TranscriptFolding folding, Action<TranscriptFolding> changed)
    {
        Translate(language);
        Preview.Open(language);
        AutomationProperties.SetName(PresetShell, Text("chat.folding_preset"));
        var presets = new (string Id, string Label, TranscriptFolding Modes)[]
        {
            ("compact", "chat.compact", new(Activity: TranscriptFoldMode.Folded)),
            ("standard", "look.standard", new()),
            ("changes", "chat.changes_only", new(Activity: TranscriptFoldMode.Changed, Steps: TranscriptFoldMode.Changed)),
            ("full", "chat.detailed", new(TranscriptFoldMode.Open, TranscriptFoldMode.Open, TranscriptFoldMode.Open, TranscriptFoldMode.Open, TranscriptFoldMode.Open)),
        };
        var presetButtons = new List<(RadioButton Button, TranscriptFolding Modes)>();
        var radios = new List<(TranscriptPart Part, TranscriptFoldMode Mode, RadioButton Button)>();
        var previewSwitch = new ToggleSwitch { IsChecked = folding.ThinkingPreview };
        AutomationProperties.SetAutomationId(previewSwitch, "action:transcript.thinking-preview");
        AutomationProperties.SetName(previewSwitch, Text("chat.preview_the_beginning_when_collapsed"));
        previewSwitch.IsCheckedChanged += (_, _) => Apply(folding with { ThinkingPreview = previewSwitch.IsChecked == true });
        foreach (var (id, label, modes) in presets)
        {
            var button = Segment(label, "action:transcript.fold-preset." + id);
            button.Click += (_, _) => Apply(modes with { ThinkingPreview = folding.ThinkingPreview });
            Presets.Children.Add(button);
            presetButtons.Add((button, modes));
        }

        var parts = new (TranscriptPart Part, string Label, string Hint, TranscriptFoldMode[] Modes)[]
        {
            (TranscriptPart.Thinking, "chat.thinking_2", "chat.the_models_reasoning_before_it", [TranscriptFoldMode.Folded, TranscriptFoldMode.Live, TranscriptFoldMode.Open]),
            (TranscriptPart.Activity, "chat.activity", "chat.a_run_of_tool_calls", [TranscriptFoldMode.Folded, TranscriptFoldMode.Live, TranscriptFoldMode.Changed, TranscriptFoldMode.Open]),
            (TranscriptPart.Steps, "chat.step_details", "chat.each_steps_arguments_output_and", [TranscriptFoldMode.Folded, TranscriptFoldMode.Failed, TranscriptFoldMode.Changed, TranscriptFoldMode.Open]),
            (TranscriptPart.Output, "chat.long_output", "chat.command_output_and_file_contents", [TranscriptFoldMode.Folded, TranscriptFoldMode.Open]),
            (TranscriptPart.Compaction, "chat.compaction_brief", "chat.the_brief_the_model_works", [TranscriptFoldMode.Folded, TranscriptFoldMode.Open]),
        };
        foreach (var (part, label, hint, modes) in parts)
        {
            var choices = new WrapPanel();
            var rail = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(2), Child = choices };
            rail[!Border.BackgroundProperty] = StudioTypography.Resource("OverlayBrush");
            AutomationProperties.SetName(rail, Text(label));
            foreach (var mode in modes)
            {
                var id = $"action:transcript.fold.{part.ToString().ToLowerInvariant()}.{mode.ToString().ToLowerInvariant()}";
                var button = Segment(ModeLabel(part, mode), id);
                button.Click += (_, _) => Apply(folding.WithMode(part, mode));
                choices.Children.Add(button);
                radios.Add((part, mode, button));
            }

            AddRow(part, label, hint, rail);
            if (part == TranscriptPart.Thinking)
            {
                AddRow(part, "chat.preview_the_beginning_when_collapsed", "chat.show_one_line_of_opening", previewSwitch);
            }
        }

        Rows.PointerExited += (_, _) => Preview.Highlight(null);
        Refresh();

        RadioButton Segment(string label, string id)
        {
            var button = new RadioButton { Content = Text(label), Classes = { "segmented" } };
            AutomationProperties.SetAutomationId(button, id);
            return button;
        }

        void AddRow(TranscriptPart part, string label, string hint, Control control)
        {
            var row = new FoldingOptionRow();
            if (ReferenceEquals(control, previewSwitch))
            {
                row.Classes.Add("preview-option");
                AutomationProperties.SetAutomationId(row, "class:lrow");
            }

            row.Classes.Set("last-option", part == TranscriptPart.Compaction);
            row.Show(Text(label), Text(hint), control);
            row.PointerEntered += (_, _) => Preview.Highlight(part);
            row.GotFocus += (_, _) => Preview.Highlight(part);
            Rows.Children.Add(row);
        }

        void Apply(TranscriptFolding next)
        {
            folding = next;
            changed(next);
            Refresh();
        }

        void Refresh()
        {
            var modes = folding with
            {
                ThinkingPreview = false,
            };
            var matchesPreset = false;
            foreach (var (button, preset) in presetButtons)
            {
                button.IsChecked = preset == modes;
                matchesPreset |= button.IsChecked == true;
            }

            foreach (var (part, mode, button) in radios)
            {
                button.IsChecked = mode == folding.Mode(part);
            }

            Custom.IsVisible = !matchesPreset;
            Preview.Show(folding);
        }
    }

    /// <summary>Names the same mode differently for output, which is clipped.</summary>
    /// <param name="part">The transcript part.</param><param name="mode">An allowed mode.</param><returns>The label key.</returns>
    private static string ModeLabel(TranscriptPart part, TranscriptFoldMode mode) => (part, mode) switch
    {
        (TranscriptPart.Output, TranscriptFoldMode.Folded) => "chat.clipped",
        (TranscriptPart.Output, TranscriptFoldMode.Open) => "look.full",
        (_, TranscriptFoldMode.Live) => "chat.open_while_running",
        (_, TranscriptFoldMode.Failed) => "chat.open_on_failure",
        (_, TranscriptFoldMode.Changed) => "chat.open_on_file_change",
        (_, TranscriptFoldMode.Open) => "common.expand",
        _ => "common.collapse",
    };
}
