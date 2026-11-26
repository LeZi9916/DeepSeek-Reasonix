using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using System;
using System.Threading;

namespace Reasonix.Studio.Pages;

/// <summary>Displays a workspace image while its owning Markdown block is mounted.</summary>
internal sealed partial class MarkdownImage : UserControl
{
    /// <summary>The active load for this mount.</summary>
    private CancellationTokenSource? _loading;

    /// <summary>Initializes a new instance of the <see cref="MarkdownImage"/> class.</summary>
    /// <param name="path">A resolved workspace file.</param><param name="caption">The alternative text.</param>
    /// <param name="assets">The owner's read capability.</param>
    internal MarkdownImage(string path, string? caption, MarkdownAssets assets)
    {
        InitializeComponent();
        Caption.Text = string.IsNullOrEmpty(caption) ? path : caption;
        AutomationProperties.SetName(Preview, Caption.Text);
        ToolTip.SetTip(this, Caption.Text);
        AttachedToVisualTree += async (_, _) =>
        {
            using var load = new CancellationTokenSource();
            _loading = load;
            try
            {
                var bitmap = await assets.ReadImage(path, load.Token).ConfigureAwait(true);
                if (load.IsCancellationRequested)
                {
                    bitmap?.Dispose();
                }
                else if (bitmap is not null)
                {
                    Preview.Source = bitmap;
                    Preview.IsVisible = true;
                    Caption.IsVisible = false;
                }
            }
            catch (OperationCanceledException) when (load.IsCancellationRequested)
            {
            }
            finally
            {
                if (ReferenceEquals(_loading, load))
                {
                    _loading = null;
                }
            }
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _loading?.Cancel();
            if (Preview.Source is Bitmap bitmap)
            {
                Preview.Source = null;
                bitmap.Dispose();
            }

            Preview.IsVisible = false;
            Caption.IsVisible = true;
        };
    }
}
