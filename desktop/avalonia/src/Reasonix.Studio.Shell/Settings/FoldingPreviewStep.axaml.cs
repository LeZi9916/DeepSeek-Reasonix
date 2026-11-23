using Avalonia.Controls;
using Avalonia.Media;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>One retained sample tool step in the folding preview.</summary>
public sealed partial class FoldingPreviewStep : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="FoldingPreviewStep"/> class.</summary>
    public FoldingPreviewStep() => InitializeComponent();

    /// <summary>Shows the reference sample in the selected disclosure state.</summary>
    /// <param name="tool">The sample tool.</param><param name="argument">The sample argument.</param>
    /// <param name="open">Whether the step is expanded.</param><param name="failed">Whether the sample failed.</param>
    /// <param name="longOutput">Whether the sample has long output.</param><param name="fullOutput">Whether the output is unclipped.</param>
    /// <param name="language">The preview language.</param>
    internal void Show(string tool, string argument, bool open, bool failed, bool longOutput, bool fullOutput, InterfaceLanguage language)
    {
        ToolName.Text = tool;
        Argument.Text = argument;
        Caret.Text = open ? "⌄" : "›";
        ToolName[!TextBlock.ForegroundProperty] = StudioTypography.Resource(failed ? "ErrInkBrush" : "MutedBrush");
        if (failed)
        {
            Detail.BorderBrush = (IBrush)this.FindResource("FailedLine")!;
        }
        else
        {
            Detail[!Border.BorderBrushProperty] = StudioTypography.Resource("HairBrush");
        }

        Detail.IsVisible = open;
        Lines.Count = longOutput ? (fullOutput ? 6 : 3) : 1;
        Lines.OpacityMask = longOutput && !fullOutput ? (IBrush)this.FindResource("OutputFade")! : null;
        More.IsVisible = longOutput;
        More.Text = MessageCatalog.Get(fullOutput ? "common.collapse" : "chat.show_all", language);
    }

    /// <summary>Highlights the step or its output without replacing the sample.</summary>
    /// <param name="step">Whether the step setting is highlighted.</param><param name="output">Whether the output setting is highlighted.</param>
    internal void Highlight(bool step, bool output)
    {
        StepFrame.Classes.Set("example-hot", step);
        Detail.Classes.Set("example-hot", output);
    }
}
