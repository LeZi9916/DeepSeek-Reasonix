using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Reasonix.Studio.Pages.I18n;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>One workbench surface and the separate action that closes it.</summary>
internal sealed partial class WorkbenchHeaderTab : UserControl
{
    /// <summary>The optional changes count in the tab label.</summary>
    private static readonly StyledProperty<string?> s_countProperty =
        AvaloniaProperty.Register<WorkbenchHeaderTab, string?>(nameof(Count));

    /// <summary>The entry motion, superseded when this tab starts closing.</summary>
    private CancellationTokenSource? _entrance;

    /// <summary>Initializes a new instance of the <see cref="WorkbenchHeaderTab"/> class.</summary>
    /// <param name="title">Visible tab title.</param>
    /// <param name="glyph">The named icon for the surface.</param>
    /// <param name="selected">Whether this surface owns the workbench.</param>
    /// <param name="language">Interface language for the close action.</param>
    internal WorkbenchHeaderTab(string title, string glyph, bool selected, InterfaceLanguage language)
    {
        InitializeComponent();
        Picker.Content = title;
        if (glyph == "Changes")
        {
            TabGlyph.Bind(Avalonia.Controls.Shapes.Path.DataProperty, this.GetResourceObservable("Glyph.git-branch"));
        }
        else if (glyph == "Job")
        {
            TabGlyph.Bind(Avalonia.Controls.Shapes.Path.DataProperty, this.GetResourceObservable("Glyph.job-play"));
        }
        else if (glyph == "Agent")
        {
            TabIcon.IsVisible = false;
            AgentIcon.IsVisible = true;
            AgentIcon.Show(title, iconSize: 11);
        }
        else if (glyph != "File")
        {
            TabGlyph.Data = this.FindResource(glyph + "Glyph") as Geometry;
        }

        Classes.Set("active", selected);
        Picker.Classes.Set("active", selected);
        if (glyph == "New")
        {
            Dismiss.IsVisible = false;
            Picker.Focusable = false;
            Picker.IsHitTestVisible = false;
            Picker.Padding = new Avalonia.Thickness(31, 0, 12, 0);
        }
        else
        {
            AutomationProperties.SetName(Dismiss, MessageCatalog.Get("common.disable_name", language, ("name", title)));
            ToolTip.SetTip(Dismiss, MessageCatalog.Get(MessageKey.Common.Close, language));
        }
    }

    /// <summary>Gets the action that selects the represented surface.</summary>
    internal Button PickButton => Picker;

    /// <summary>Gets the action that closes the represented surface.</summary>
    internal Button CloseButton => Dismiss;

    /// <summary>Gets the count displayed for the changes tab.</summary>
    internal string? Count => GetValue(s_countProperty);

    /// <summary>Displays the count for the current changes board.</summary>
    /// <param name="board">The board that owns the authoritative count.</param>
    internal void ShowCount(GitChangesView board) => Bind(s_countProperty, board.GetObservable(GitChangesView.TabCountProperty));

    /// <summary>Applies the reference colors to the file categories marked in the tab strip.</summary>
    /// <param name="path">The canonical file path.</param>
    internal void ColorFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        TabGlyph.Classes.Set("go", extension == ".go");
        TabGlyph.Classes.Set("python", extension == ".py");
        TabGlyph.Classes.Set("sheet", extension is ".xls" or ".xlsx" or ".csv" or ".tsv");
    }

    /// <summary>Animates a tab created by an explicit open.</summary>
    /// <param name="lifetime">The window's lifetime.</param>
    internal async void Enter(CancellationToken lifetime)
    {
        using var motion = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _entrance = motion;
        DetachedFromVisualTree += Cancel;
        ClipToBounds = true;
        var duration = TimeSpan.FromMilliseconds(180);
        var easing = new SplineEasing(0.16, 1, 0.3, 1);
        try
        {
            await Task.WhenAll(
                AnimateTabValue(OpacityProperty, 0, 1, duration, easing, motion.Token),
                AnimateTabValue(MinWidthProperty, 0, MinWidth, duration, easing, motion.Token),
                AnimateTabValue(MaxWidthProperty, 0, MaxWidth, duration, easing, motion.Token)).ConfigureAwait(true);
            if (Classes.Contains("active"))
            {
                this.BringIntoView();
            }
        }
        catch (OperationCanceledException) when (motion.IsCancellationRequested)
        {
        }
        finally
        {
            if (_entrance == motion)
            {
                ClipToBounds = false;
                _entrance = null;
            }

            DetachedFromVisualTree -= Cancel;
        }

        void Cancel(object? sender, VisualTreeAttachmentEventArgs args) => motion.Cancel();
    }

    /// <summary>Shrinks the tab before executing its explicit close action.</summary>
    /// <param name="lifetime">The window's lifetime.</param>
    /// <returns>Whether this request should commit the close.</returns>
    internal async Task<bool> LeaveAsync(CancellationToken lifetime)
    {
        if (Classes.Contains("leaving"))
        {
            return false;
        }

        if (TopLevel.GetTopLevel(this) is not Window window || !window.Classes.Contains("motion"))
        {
            return !lifetime.IsCancellationRequested;
        }

        using var motion = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        var width = Bounds.Width;
        var opacity = Opacity;
        var entrance = _entrance;
        _entrance = null;
        entrance?.Cancel();
        Classes.Add("leaving");
        ClipToBounds = true;
        IsHitTestVisible = false;
        DetachedFromVisualTree += Cancel;
        window.Classes.CollectionChanged += MotionChanged;
        var duration = TimeSpan.FromMilliseconds(140);
        var easing = new SplineEasing(0, 0, 0.58, 1);
        try
        {
            await Task.WhenAll(
                AnimateTabValue(OpacityProperty, opacity, 0, duration, easing, motion.Token),
                AnimateTabValue(MinWidthProperty, 0, 0, duration, easing, motion.Token),
                AnimateTabValue(MaxWidthProperty, width, 0, duration, easing, motion.Token)).ConfigureAwait(true);
            return !lifetime.IsCancellationRequested;
        }
        catch (OperationCanceledException) when (motion.IsCancellationRequested)
        {
            return !lifetime.IsCancellationRequested;
        }
        finally
        {
            DetachedFromVisualTree -= Cancel;
            window.Classes.CollectionChanged -= MotionChanged;
            Classes.Remove("leaving");
            IsHitTestVisible = true;
            ClipToBounds = false;
        }

        void Cancel(object? sender, VisualTreeAttachmentEventArgs args) => motion.Cancel();

        void MotionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
        {
            if (!window.Classes.Contains("motion"))
            {
                motion.Cancel();
            }
        }
    }

    /// <summary>Shows the unsaved dot in the close slot until the tab is hovered.</summary>
    /// <param name="dirty">Whether the represented file has unsaved edits.</param>
    internal void ShowDirty(bool dirty) => Classes.Set("dirty", dirty);

    /// <summary>Runs one channel so detaching a tab cannot complete a shared Avalonia animator twice.</summary>
    /// <param name="property">The opacity or width being rendered.</param>
    /// <param name="from">The visible starting value.</param>
    /// <param name="to">The value reached when the tab settles.</param>
    /// <param name="duration">The reference timing.</param>
    /// <param name="easing">The reference curve.</param>
    /// <param name="token">Cancels motion on dismissal or teardown.</param>
    /// <returns>The individual animation.</returns>
    private Task AnimateTabValue(StyledProperty<double> property, double from, double to, TimeSpan duration, Easing easing, CancellationToken token) =>
        new Animation
        {
            Duration = duration,
            Easing = easing,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(property, from) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(property, to) } },
            },
        }.RunAsync(this, token);
}
