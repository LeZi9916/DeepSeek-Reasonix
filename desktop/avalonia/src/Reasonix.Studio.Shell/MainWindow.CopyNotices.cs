using Avalonia;
using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Notices;
using System;
using System.Linq;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Publishes the same clipboard note as the reference transcript.</summary>
    /// <param name="succeeded">Whether the platform clipboard accepted the selection.</param>
    private void ShowCopyNotice(bool succeeded)
    {
        if (_closing.IsCancellationRequested)
        {
            return;
        }

        var message = MessageCatalog.Get(succeeded ? MessageKey.Common.Copied : MessageKey.Common.CopyFailedTryAgain, Language);
        if (CopyNotices.Children.OfType<CopyNotice>().Any(notice => notice.Caption == message))
        {
            return;
        }

        while (CopyNotices.Children.Count >= 3)
        {
            CopyNotices.Children.RemoveAt(0);
        }

        var row = new CopyNotice();
        row.Dismissed += notice => CopyNotices.Children.Remove(notice);
        CopyNotices.Children.Add(row);
        PositionCopyNotices();
        row.Show(message, MessageCatalog.Get(MessageKey.Common.GotIt, Language), Classes.Contains("motion"));
    }

    /// <summary>Keeps the stack just above the composer at its trailing edge.</summary>
    private void PositionCopyNotices()
    {
        if (CopyNotices.Children.Count == 0)
        {
            return;
        }

        if (ComposeSurface.IsEffectivelyVisible &&
            ComposeSurface.TranslatePoint(new Point(ComposeSurface.Bounds.Width, 0), CopyNoticeLayer) is { } above)
        {
            Canvas.SetRight(CopyNotices, Math.Max(16, Math.Round(CopyNoticeLayer.Bounds.Width - above.X)));
            Canvas.SetBottom(CopyNotices, Math.Round(CopyNoticeLayer.Bounds.Height - above.Y + 8));
            CopyNotices.Width = Math.Min(380, ComposeSurface.Bounds.Width);
        }
        else
        {
            Canvas.SetRight(CopyNotices, 16);
            Canvas.SetBottom(CopyNotices, 16);
            CopyNotices.Width = Math.Max(0, Math.Min(380, CopyNoticeLayer.Bounds.Width - 32));
        }
    }
}
