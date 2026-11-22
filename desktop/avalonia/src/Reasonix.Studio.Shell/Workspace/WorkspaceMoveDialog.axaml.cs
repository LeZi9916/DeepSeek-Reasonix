using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Workspace;

/// <summary>Moves a transcript to a known project and retains the owner's refusal or partial outcome.</summary>
public sealed partial class WorkspaceMoveDialog : UserControl
{
    /// <summary>The canonical mutation for the current dialog.</summary>
    private Func<TreeWorkspace, Task<bool>>? _move;

    /// <summary>The current interface language.</summary>
    private InterfaceLanguage _language;

    /// <summary>Initializes a new instance of the <see cref="WorkspaceMoveDialog"/> class.</summary>
    public WorkspaceMoveDialog()
    {
        InitializeComponent();
        Cancel.Click += (_, _) => IsVisible = false;
        AddProject.Click += (_, _) =>
        {
            IsVisible = false;
            AddRequested?.Invoke();
        };
        MoveSession.Click += async (_, _) => await MoveAsync().ConfigureAwait(true);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && Cancel.IsEnabled)
            {
                IsVisible = false;
                e.Handled = true;
            }
        };
    }

    /// <summary>Raised when an empty destination list asks for the existing folder picker.</summary>
    public event Action? AddRequested;

    /// <summary>Shows only canonical remembered destinations outside the source project.</summary>
    /// <param name="projects">Canonical destinations.</param><param name="name">Conversation display name.</param>
    /// <param name="move">Returns whether the source was removed.</param><param name="language">Interface language.</param>
    public void Open(IReadOnlyList<TreeWorkspace> projects, string name, Func<TreeWorkspace, Task<bool>> move, InterfaceLanguage language)
    {
        _move = move;
        _language = language;
        Title.Text = MessageCatalog.Get("session.move_name_to_another_project", language, ("name", name));
        TargetLabel.Text = MessageCatalog.Get("session.target_project", language);
        EmptyLabel.Text = MessageCatalog.Get("session.there_is_no_other_project", language);
        AddProject.Content = MessageCatalog.Get("session.add_a_project", language);
        TravelsLabel.Text = MessageCatalog.Get("session.travels", language);
        StaysLabel.Text = MessageCatalog.Get("session.stays_behind", language);
        TravelsDetail.Text = string.Join(Environment.NewLine, new[] { "session.the_conversation_itself", "session.the_title_pin_and_archive", "session.paths_recorded_in_the_conversation" }.Select(key => MessageCatalog.Get(key, language)));
        StaysDetail.Text = string.Join(Environment.NewLine, new[] { "session.checkpoints_rewind_snapshots", "session.background_task_logs", "session.queued_messages" }.Select(key => MessageCatalog.Get(key, language)));
        PolicyNote.Text = MessageCatalog.Get("session.what_the_old_project_allowed", language);
        Cancel.Content = MessageCatalog.Get("common.cancel", language);
        MoveSession.Content = MessageCatalog.Get("session.move", language);
        ProjectChoices.ItemsSource = projects;
        ProjectChoices.SelectedIndex = projects.Count > 0 ? 0 : -1;
        ProjectChoices.IsVisible = Travels.IsVisible = PolicyNote.IsVisible = TargetLabel.IsVisible = projects.Count > 0;
        EmptyProjects.IsVisible = projects.Count == 0;
        Failure.IsVisible = MoveNote.IsVisible = false;
        ProjectChoices.IsEnabled = Cancel.IsEnabled = true;
        MoveSession.IsEnabled = projects.Count > 0;
        IsVisible = true;
        Dispatcher.UIThread.Post(() => (projects.Count > 0 ? (Control)ProjectChoices : Cancel).Focus());
    }

    /// <summary>Submits only the selected canonical root and preserves a partial acknowledgement.</summary>
    /// <returns>The completed move attempt.</returns>
    private async Task MoveAsync()
    {
        if (_move is null || ProjectChoices.SelectedItem is not TreeWorkspace project)
        {
            return;
        }

        ProjectChoices.IsEnabled = Cancel.IsEnabled = MoveSession.IsEnabled = false;
        Failure.IsVisible = false;
        try
        {
            if (await _move(project).ConfigureAwait(true))
            {
                IsVisible = false;
            }
            else
            {
                MoveNote.Text = MessageCatalog.Get("session.the_conversation_was_moved_but", _language);
                MoveNote.IsVisible = true;
                Cancel.Content = MessageCatalog.Get("common.close", _language);
            }
        }
        catch (Exception error)
        {
            Failure.Text = error.Message;
            Failure.IsVisible = true;
        }
        finally
        {
            ProjectChoices.IsEnabled = Cancel.IsEnabled = true;
            MoveSession.IsEnabled = !MoveNote.IsVisible;
        }
    }
}
