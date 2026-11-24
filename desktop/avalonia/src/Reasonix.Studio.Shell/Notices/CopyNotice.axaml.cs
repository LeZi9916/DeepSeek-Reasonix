using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using System;

namespace Reasonix.Studio.Shell.Notices;

/// <summary>Shows the reference's dismissible note after a clipboard action.</summary>
internal sealed partial class CopyNotice : Border
{
    /// <summary>Initializes a new instance of the <see cref="CopyNotice"/> class.</summary>
    public CopyNotice()
    {
        InitializeComponent();
        Dismiss.Click += (_, _) => Dismissed?.Invoke(this);
    }

    /// <summary>Raised when the reader acknowledges the notice.</summary>
    internal event Action<CopyNotice>? Dismissed;

    /// <summary>Gets the notice text used for deduplication.</summary>
    internal string Caption => Message.Text ?? string.Empty;

    /// <summary>Publishes the note and plays its entrance without taking focus.</summary>
    /// <param name="caption">The localized outcome.</param>
    /// <param name="dismiss">The localized acknowledgement.</param>
    /// <param name="motion">Whether the owning window allows motion.</param>
    internal void Show(string caption, string dismiss, bool motion)
    {
        Message.Text = caption;
        Dismiss.Content = dismiss;
        AutomationProperties.SetName(this, caption);
        BoxShadow = BoxShadows.Parse(ActualThemeVariant == ThemeVariant.Dark
            ? "0 22 60 -22 #D9000000, 0 6 18 -6 #59000000"
            : "0 18 44 -18 #33171E2D, 0 4 12 -8 #24171E2D");
        if (motion)
        {
            Classes.Add("arriving");
            DispatcherTimer.RunOnce(() => Classes.Remove("arriving"), TimeSpan.FromMilliseconds(180));
        }
    }
}
