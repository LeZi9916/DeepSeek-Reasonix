using Avalonia.Media;
using System;

namespace Reasonix.Studio.Pages;

public sealed partial class SessionPage
{
    /// <summary>The cover for a fully loaded, empty conversation.</summary>
    private readonly ConversationHero _hero = new();

    /// <summary>Whether the transcript has incomplete or failed history.</summary>
    private bool _historyOutstanding;

    /// <summary>Whether the workspace tree requires a first project choice.</summary>
    private bool _needsProject;

    /// <summary>Gets or sets the current workspace's Markdown file capabilities.</summary>
    public MarkdownAssets? WorkspaceAssets
    {
        get;
        set;
    }

    /// <summary>Connects the empty conversation actions to the owning window.</summary>
    /// <param name="mark">Studio's brand geometry.</param><param name="open">Opens the project picker.</param>
    /// <param name="keep">Accepts the current workspace.</param><param name="suggest">Inserts a draft prompt.</param>
    public void ConfigureConversationHero(Geometry? mark, Action open, Action keep, Action<string> suggest) => _hero.Configure(mark, open, keep, suggest);

    /// <summary>Selects the project or prompt variant of the empty conversation.</summary>
    /// <param name="needsProject">Whether the user has yet to choose a workspace.</param>
    public void SetHeroProjectChoice(bool needsProject)
    {
        _needsProject = needsProject;
        UpdateConversationHero();
    }

    /// <summary>Withholds the cover while there may still be history to display.</summary>
    /// <param name="outstanding">Whether the history is incomplete.</param>
    public void SetHeroHistoryOutstanding(bool outstanding)
    {
        _historyOutstanding = outstanding;
        UpdateConversationHero();
    }

    /// <summary>Shows the localized cover once an empty transcript is settled.</summary>
    private void UpdateConversationHero()
    {
        _hero.IsVisible = !_historyOutstanding && !_state.Running && _state.Items.Count == 0 && _state.ResponseWait is null;
        if (_hero.IsVisible)
        {
            _hero.Present(Language, _needsProject);
        }
    }
}
