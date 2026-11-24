using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Composer;

/// <summary>A collapsible view of the acknowledged plan, docked above the composer.</summary>
public sealed partial class PlanStrip : UserControl
{
    /// <summary>The last acknowledged list displayed by the strip.</summary>
    private IReadOnlyList<PlanStep> _plan = [];

    /// <summary>The presentation inputs from the last visible update.</summary>
    private (bool Running, bool Progress, InterfaceLanguage Language)? _presentation;

    /// <summary>The animation of the latest explicit expand or collapse.</summary>
    private CancellationTokenSource? _transition;

    /// <summary>The requested mode, including while the previous view exits.</summary>
    private bool _expanded;

    /// <summary>Initializes a new instance of the <see cref="PlanStrip"/> class.</summary>
    public PlanStrip()
    {
        InitializeComponent();
        Summary.Click += (_, _) => SetExpanded(true, restoreFocus: false);
        Close.Click += (_, _) => Collapse(restoreFocus: true);
        DetachedFromVisualTree += (_, _) => Reset();
    }

    /// <summary>Gets a value indicating whether the reader expanded the list.</summary>
    internal bool IsExpanded => _expanded;

    /// <summary>Updates the strip from the same snapshot used by the rest of the composer.</summary>
    /// <param name="plan">The canonical task list.</param><param name="running">Whether the turn is running.</param>
    /// <param name="paused">Whether the turn is waiting for a decision.</param><param name="progress">Whether the collapsed progress is shown.</param>
    /// <param name="language">The reader's language.</param>
    internal void Show(IReadOnlyList<PlanStep> plan, bool running, bool paused, bool progress, InterfaceLanguage language)
    {
        if (plan.Count == 0 || paused)
        {
            if (IsVisible)
            {
                Reset();
                IsVisible = false;
            }

            return;
        }

        var presentation = (running, progress, language);
        if (IsVisible && ReferenceEquals(_plan, plan) && _presentation == presentation)
        {
            return;
        }

        _presentation = presentation;
        IsVisible = true;
        var current = -1;
        var pending = -1;
        var done = 0;
        for (var index = 0; index < plan.Count; index++)
        {
            switch (plan[index].Status)
            {
                case TodoStatus.Completed:
                    done += 1;
                    break;
                case TodoStatus.InProgress when current < 0:
                    current = index;
                    break;
                case TodoStatus.Pending when pending < 0:
                    pending = index;
                    break;
            }
        }

        current = current < 0 ? pending : current;
        var ordinal = current < 0 ? Math.Min(done + 1, plan.Count) : current + 1;
        var title = MessageCatalog.Get("plan.strip.title", language, ("n", Format.Count(ordinal, language)), ("total", Format.Count(plan.Count, language)));
        SummaryTitle.Text = BodyTitle.Text = title;
        var step = plan[Math.Max(current, 0)];
        CurrentLabel.Text = step.Status == TodoStatus.InProgress && !string.IsNullOrEmpty(step.ActiveForm) ? step.ActiveForm : step.Text;
        CompactMark.IsVisible = ExpandedMark.IsVisible = running;
        Classes.Set("moving", running);
        ToolTip.SetTip(Close, MessageCatalog.Get("plan.strip.collapse", language));
        AutomationProperties.SetName(Close, MessageCatalog.Get("plan.strip.collapse", language));
        AutomationProperties.SetName(Summary, title + " · " + CurrentLabel.Text);
        AutomationProperties.SetName(ProgressValue, MessageCatalog.Get("plan.strip.progress", language));
        ProgressValue.Maximum = plan.Count;
        ProgressValue.Value = done;
        Segments.IsVisible = progress;
        ProgressValue.IsVisible = Summary.IsVisible && progress;
        Grid.SetColumnSpan(CurrentLabel, progress ? 1 : 2);

        if (!ReferenceEquals(_plan, plan) || Steps.Children.Count != plan.Count)
        {
            UpdateRows(plan);
        }

        for (var index = 0; index < plan.Count; index++)
        {
            ((PlanStepRow)Steps.Children[index]).Show(plan[index], index + 1, index == current, running, language);
            var segment = (Border)Segments.Children[index];
            segment[!Border.BackgroundProperty] = StudioTypography.Resource(
                plan[index].Status == TodoStatus.Completed ? "OkBrush" : index == current ? "NetBrush" : "BorderBrush");
        }

        _plan = plan;
    }

    /// <summary>Puts the strip away when focus leaves or Escape is pressed.</summary>
    /// <param name="restoreFocus">Whether keyboard focus returns to the summary.</param>
    internal void Collapse(bool restoreFocus) => SetExpanded(false, restoreFocus);

    /// <summary>Returns to the compact view when its owning pane changes.</summary>
    internal void Reset()
    {
        _transition?.Cancel();
        _expanded = false;
        SetBodyVisible(false);
    }

    /// <summary>Fits the expanded list within the reference viewport budget.</summary>
    /// <param name="height">Available window height.</param>
    internal void SetViewport(double height) => Body.MaxHeight = Math.Min(380, height * 0.46);

    /// <summary>Keeps plan rows by their text and duplicate order as the kernel rewrites the list.</summary>
    /// <param name="plan">The new canonical plan.</param>
    private void UpdateRows(IReadOnlyList<PlanStep> plan)
    {
        var reusable = new Dictionary<string, Queue<PlanStepRow>>(StringComparer.Ordinal);
        for (var index = 0; index < _plan.Count && index < Steps.Children.Count; index++)
        {
            var text = _plan[index].Text;
            if (!reusable.TryGetValue(text, out var queue))
            {
                queue = new Queue<PlanStepRow>();
                reusable.Add(text, queue);
            }

            queue.Enqueue((PlanStepRow)Steps.Children[index]);
        }

        for (var index = 0; index < plan.Count; index++)
        {
            var row = reusable.TryGetValue(plan[index].Text, out var queue) && queue.TryDequeue(out var retained) ? retained : new PlanStepRow();
            var oldIndex = Steps.Children.IndexOf(row);
            if (oldIndex < 0)
            {
                Steps.Children.Insert(index, row);
            }
            else if (oldIndex != index)
            {
                Steps.Children.Move(oldIndex, index);
            }
        }

        while (Steps.Children.Count > plan.Count)
        {
            Steps.Children.RemoveAt(Steps.Children.Count - 1);
        }

        while (Segments.Children.Count > plan.Count)
        {
            Segments.Children.RemoveAt(Segments.Children.Count - 1);
        }

        while (Segments.Children.Count < plan.Count)
        {
            Segments.Children.Add(new Border { Width = 14, Height = 4, CornerRadius = new CornerRadius(2) });
        }
    }

    /// <summary>Starts an explicit user fold and returns focus when the close completes.</summary>
    /// <param name="expanded">The requested view.</param><param name="restoreFocus">Whether to focus the summary after closing.</param>
    private void SetExpanded(bool expanded, bool restoreFocus)
    {
        if (!IsVisible || _expanded == expanded)
        {
            return;
        }

        _expanded = expanded;
        _transition?.Cancel();
        if (TopLevel.GetTopLevel(this) is not Window window || !window.Classes.Contains("motion"))
        {
            SettleFold(expanded, restoreFocus);

            return;
        }

        _ = AnimateFoldAsync(window, expanded, restoreFocus);
    }

    /// <summary>Animates the plan's reserved height while it opens or exits.</summary>
    /// <param name="window">The window that owns the motion preference.</param>
    /// <param name="expanded">The requested view.</param>
    /// <param name="restoreFocus">Whether the summary receives keyboard focus.</param>
    /// <returns>Completion of the current fold.</returns>
    private async Task AnimateFoldAsync(Window window, bool expanded, bool restoreFocus)
    {
        const double CompactHeight = 34;
        using var transition = new CancellationTokenSource();
        _transition = transition;
        SetBodyVisible(true);
        Body.IsHitTestVisible = expanded;
        StepsScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        Body.Measure(new Size(Math.Max(1, Bounds.Width - 2), double.PositiveInfinity));

        // The CSS close keyframe starts from the natural height even if opening was interrupted.
        var naturalHeight = Math.Max(CompactHeight, Body.DesiredSize.Height);
        var duration = TimeSpan.FromMilliseconds(expanded ? 260 : 120);
        var easing = expanded ? new SplineEasing(0.16, 1, 0.3, 1) : new SplineEasing(0.55, 0, 1, 0.45);
        window.Classes.CollectionChanged += MotionChanged;

        try
        {
            // One animator per task keeps visual-tree teardown from completing the same task twice.
            await Task.WhenAll(
                AnimateFoldValue(HeightProperty, expanded ? CompactHeight : naturalHeight, expanded ? naturalHeight : CompactHeight, duration, easing, transition.Token),
                AnimateFoldValue(OpacityProperty, expanded ? 0.4 : 1, expanded ? 1 : 0, duration, easing, transition.Token)).ConfigureAwait(true);
            if (!transition.IsCancellationRequested)
            {
                SettleFold(expanded, restoreFocus);
            }
        }
        catch (OperationCanceledException) when (transition.IsCancellationRequested)
        {
        }
        finally
        {
            window.Classes.CollectionChanged -= MotionChanged;
            if (ReferenceEquals(_transition, transition))
            {
                _transition = null;
            }
        }

        void MotionChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (!window.Classes.Contains("motion") && ReferenceEquals(_transition, transition) && !transition.IsCancellationRequested)
            {
                transition.Cancel();
                SettleFold(expanded, restoreFocus);
            }
        }
    }

    /// <summary>Runs an individual height or opacity keyframe animation.</summary>
    /// <param name="property">The animated property.</param>
    /// <param name="from">The starting value.</param>
    /// <param name="to">The final value.</param>
    /// <param name="duration">The reference timing.</param>
    /// <param name="easing">The reference curve.</param>
    /// <param name="token">Cancels the current fold.</param>
    /// <returns>The individual animation.</returns>
    private Task AnimateFoldValue(StyledProperty<double> property, double from, double to, TimeSpan duration, Easing easing, CancellationToken token) =>
        new Animation
        {
            Duration = duration,
            Easing = easing,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(property, from) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(property, to) } },
            },
        }.RunAsync(Body, token);

    /// <summary>Publishes the settled view before returning focus to the summary.</summary>
    /// <param name="expanded">Whether to show the expanded list.</param>
    /// <param name="restoreFocus">Whether to focus the summary after closing.</param>
    private void SettleFold(bool expanded, bool restoreFocus)
    {
        SetBodyVisible(expanded);
        if (!expanded && restoreFocus)
        {
            Summary.Focus();
        }
    }

    /// <summary>Switches between the compact row and the retained list.</summary>
    /// <param name="visible">Whether to present the expanded list.</param>
    private void SetBodyVisible(bool visible)
    {
        var hadFocus = Summary.IsFocused;
        Body.IsVisible = visible;
        Body.IsHitTestVisible = visible;
        StepsScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Summary.IsVisible = !visible;
        ProgressValue.IsVisible = !visible && Segments.IsVisible;
        Surface.Classes.Set("expanded", visible);
        if (visible && hadFocus)
        {
            Close.Focus();
        }
    }
}
