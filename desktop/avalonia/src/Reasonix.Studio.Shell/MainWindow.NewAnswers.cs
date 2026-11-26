using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell.Session;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Last viewed cutoffs retained in this window after the host clears each unread mark.</summary>
    private readonly Dictionary<(string Host, string Path), long> _unreadSince = new();

    /// <summary>Connects the conversation to the focused pane's unread span.</summary>
    /// <param name="pane">The retained pane.</param>
    private void BindAnswerBoundary(SessionPaneState pane)
    {
        var key = (pane.Host, pane.Status.SessionPath.ToUpperInvariant());
        pane.AnswerBoundary.Remember(_unreadSince.TryGetValue(key, out var viewedAt) ? viewedAt : null);
        Conversation.AnswerBoundary = pane.AnswerBoundary;
    }

    /// <summary>Records the cutoff from the canonical rail row before acknowledging it.</summary>
    /// <param name="pane">The pane being viewed.</param>
    /// <returns>Whether a new cutoff was recorded.</returns>
    private bool RememberUnreadSince(SessionPaneState pane)
    {
        var path = pane.Status.SessionPath;
        var row = Projects.SearchEntries.FirstOrDefault(entry => entry.Target.Host == pane.Host && RailPresenter.Same(entry.Target.SessionPath, path)).Session;
        if (row is not { Unread: true, ViewedAt: > 0 } || row.ViewedAt is not long viewedAt)
        {
            return false;
        }

        var key = (pane.Host, path.ToUpperInvariant());
        if (_unreadSince.TryGetValue(key, out var previous) && previous == viewedAt)
        {
            return false;
        }

        _unreadSince[key] = viewedAt;
        pane.AnswerBoundary.Remember(viewedAt);
        return true;
    }

    /// <summary>Reads an unread mark that arrives after the pane is already in front.</summary>
    private void ObserveUnreadSpan()
    {
        ReflectUnreadBadge();
        if (_activePane is { } pane && IsActive && ConversationArea.IsEffectivelyVisible && RememberUnreadSince(pane))
        {
            Conversation.RefreshNewAnswers();
            _ = MarkPaneViewedAsync(pane);
        }
    }

    /// <summary>Adds the notices preference for the divider.</summary>
    /// <param name="card">The notices settings.</param>
    private void AddNewAnswerMarkerSetting(StackPanel card)
    {
        var option = new ToggleSwitch { IsChecked = _workspacePreferences.ShowNewAnswerMarker };
        AutomationProperties.SetAutomationId(option, "action:appearance.new-answer-marker");
        AddRow(card, "settings.new_answer_divider", option, MessageCatalog.Get("settings.divider_before_new_answer", Language));
        option.IsCheckedChanged += (_, _) =>
        {
            _workspacePreferences = _workspacePreferences with
            {
                ShowNewAnswerMarker = option.IsChecked == true,
            };
            if (_connectOnOpen)
            {
                _workspacePreferences.Save();
            }

            Conversation.SetShowsNewAnswers(_workspacePreferences.ShowNewAnswerMarker);
        };
    }
}
