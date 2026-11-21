using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>Groups signals within one 22-pixel hit target on the shared zoomed timeline.</summary>
public sealed partial class SignalRail : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="SignalRail"/> class.</summary>
    public SignalRail() => InitializeComponent();

    /// <summary>Binds signal markers to the measured timeline width.</summary>
    /// <param name="events">The point events in trace order.</param>
    /// <param name="span">The time axis in milliseconds.</param>
    /// <param name="inspect">Shows the selected source record.</param>
    internal void Open(IEnumerable<RunTimeline.Entry> events, double span, Action<RunTimeline.Entry> inspect)
    {
        var entries = events.OrderBy(e => e.Start).ToArray();
        RunTimeline.Entry? selected = null;
        Markers.SizeChanged += (_, _) => Render();
        Render();

        void Render()
        {
            Markers.Children.Clear();
            var width = Markers.Bounds.Width;
            var groups = new List<(double Left, List<RunTimeline.Entry> Items)>();
            foreach (var entry in entries)
            {
                var x = width * Math.Clamp(entry.Start / Math.Max(1, span), 0, 1);
                if (groups.Count == 0 || x - groups[^1].Left >= 22)
                {
                    groups.Add((x, []));
                }

                groups[^1].Items.Add(entry);
            }

            foreach (var group in groups)
            {
                var count = group.Items.Count;
                var marker = new Button { Content = count == 1 ? "●" : count.ToString(CultureInfo.CurrentCulture), Width = 22, Height = 22, Padding = default, CornerRadius = new Avalonia.CornerRadius(11) };
                AutomationProperties.SetAutomationId(marker, "action:analysis.signal");
                AutomationProperties.SetName(marker, string.Join(" · ", group.Items.Select(e => e.Label)));
                Canvas.SetLeft(marker, Math.Clamp(group.Left - 11, 0, Math.Max(0, width - 22)));
                Canvas.SetTop(marker, 4);
                marker.Click += (_, _) =>
                {
                    selected = group.Items[0];
                    ShowMembers(group.Items);
                    inspect(selected);
                };
                Markers.Children.Add(marker);
                if (selected is not null && group.Items.Any(e => ReferenceEquals(e, selected)))
                {
                    ShowMembers(group.Items);
                }
            }
        }

        void ShowMembers(List<RunTimeline.Entry> group)
        {
            Members.Children.Clear();
            foreach (var entry in group)
            {
                var button = new Button { Content = entry.Label, [!TextBlock.FontSizeProperty] = StudioTypography.Size(12) };
                AutomationProperties.SetAutomationId(button, "action:analysis.signal-member");
                button.Click += (_, _) =>
                {
                    selected = entry;
                    inspect(entry);
                };
                Members.Children.Add(button);
            }
        }
    }
}
