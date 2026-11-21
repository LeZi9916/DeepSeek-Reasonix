using Avalonia.Controls;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>Hosts persistent Git commands and review above an independently scrolling patch list.</summary>
public sealed partial class GitChangesView : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="GitChangesView"/> class.</summary>
    public GitChangesView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => ReviewViewport.MaxHeight = System.Math.Max(120, e.NewSize.Height - 80);
    }

    /// <summary>Gets the fixed command bar.</summary>
    internal ContentControl Commands => CommandHost;

    /// <summary>Gets the review shared across status refreshes.</summary>
    internal StackPanel Review => ReviewHost;

    /// <summary>Gets mutation and read feedback.</summary>
    internal TextBlock Notice => Feedback;

    /// <summary>Gets the scrolling changed-file list.</summary>
    internal StackPanel Files => ChangeFiles;
}
