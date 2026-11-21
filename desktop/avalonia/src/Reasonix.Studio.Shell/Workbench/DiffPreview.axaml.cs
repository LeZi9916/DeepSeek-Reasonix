using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using System;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>A selectable unified diff that retains the original patch text and line endings.</summary>
public sealed partial class DiffPreview : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="DiffPreview"/> class.</summary>
    public DiffPreview() => InitializeComponent();

    /// <summary>Draws a workspace diff with the active palette.</summary>
    /// <param name="patch">The exact kernel patch or the empty-state message.</param>
    internal void Show(string patch)
    {
        var inline = Patch.Inlines!;
        inline.Clear();
        for (var start = 0; start < patch.Length;)
        {
            var lf = patch.IndexOf('\n', start);
            var end = lf < 0 ? patch.Length : lf + 1;
            var line = patch[start..end];
            var key = line[0] switch
            {
                '@' when line.StartsWith("@@", StringComparison.Ordinal) => "LinkBrush",
                '+' when !line.StartsWith("+++ ", StringComparison.Ordinal) => "AddBrush",
                '-' when !line.StartsWith("--- ", StringComparison.Ordinal) => "DelBrush",
                '+' or '-' => "MutedBrush",
                'd' when line.StartsWith("diff --", StringComparison.Ordinal) => "MutedBrush",
                _ => "TextBrush",
            };
            var run = new Run(line);
            run.Bind(TextElement.ForegroundProperty, Patch.GetResourceObservable(key));
            inline.Add(run);
            start = end;
        }
    }
}
