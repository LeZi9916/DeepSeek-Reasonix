using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State.Transcript;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>The schematic turn used to preview expansion defaults before applying them to real rows.</summary>
public sealed partial class FoldingPreview : SettingsFormView
{
    /// <summary>The modes shared with the transcript renderer.</summary>
    private TranscriptFolding _folding = new();

    /// <summary>Initializes a new instance of the <see cref="FoldingPreview"/> class.</summary>
    public FoldingPreview()
    {
        InitializeComponent();
        Live.Click += (_, _) => SetScenario(live: true);
        Done.Click += (_, _) => SetScenario(live: false);
    }

    /// <summary>Sets the preview language and its initial completed sample.</summary>
    /// <param name="language">The settings language.</param>
    internal void Open(InterfaceLanguage language)
    {
        Translate(language);
        SetScenario(live: false);
    }

    /// <summary>Updates the retained sample to match the current defaults.</summary>
    /// <param name="folding">The chosen expansion policy.</param>
    internal void Show(TranscriptFolding folding)
    {
        _folding = folding;
        DrawScenario(Live.IsChecked == true);
    }

    /// <summary>Highlights the sample corresponding to the focused preference.</summary>
    /// <param name="part">The hovered or focused part, when present.</param>
    internal void Highlight(TranscriptPart? part)
    {
        ThoughtFrame.Classes.Set("example-hot", part == TranscriptPart.Thinking);
        WorkFrame.Classes.Set("example-hot", part == TranscriptPart.Activity);
        CompactionFrame.Classes.Set("example-hot", part == TranscriptPart.Compaction);
        foreach (var step in new[] { ReadStep, EditStep, ShellStep })
        {
            step.Highlight(part == TranscriptPart.Steps, part == TranscriptPart.Output);
        }
    }

    /// <summary>Chooses the running or completed sample without changing a preference.</summary>
    /// <param name="live">Whether the sample turn is running.</param>
    private void SetScenario(bool live)
    {
        Live.IsChecked = live;
        Done.IsChecked = !live;
        DrawScenario(live);
    }

    /// <summary>Applies the transcript expansion rules to the sample's known facts.</summary>
    /// <param name="live">Whether the preview is running.</param>
    private void DrawScenario(bool live)
    {
        var thinking = _folding.StartsOpen(TranscriptPart.Thinking, live: live);
        var activity = _folding.StartsOpen(TranscriptPart.Activity, live: live, changed: true);
        var compaction = _folding.StartsOpen(TranscriptPart.Compaction);
        ThoughtCaret.Text = thinking ? "⌄" : "›";
        ThoughtLabel.Text = Text(live ? "chat.thinking" : "chat.thought_for_6s");
        ThoughtLines.IsVisible = thinking;
        ThoughtPreview.IsVisible = !thinking && _folding.ThinkingPreview;
        AnswerLines.IsVisible = !live;
        WorkCaret.Text = activity ? "⌄" : "›";
        WorkStatus.Text = Text(live ? "common.running" : "chat.n_3_actions_1_failed");
        Steps.IsVisible = activity;
        ReadStep.Show("read_file", "src/app.ts", _folding.StartsOpen(TranscriptPart.Steps), false, false, false, Language);
        EditStep.Show("edit_file", "src/app.ts", _folding.StartsOpen(TranscriptPart.Steps, changed: true), false, false, false, Language);
        ShellStep.Show("bash", "npm test", _folding.StartsOpen(TranscriptPart.Steps, failed: !live), !live, true, _folding.StartsOpen(TranscriptPart.Output), Language);
        CompactionCaret.Text = compaction ? "⌄" : "›";
        CompactionLines.IsVisible = compaction;
    }
}
