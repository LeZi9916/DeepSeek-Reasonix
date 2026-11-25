using Avalonia.Input;
using Reasonix.Studio.Pages.I18n;
using System.Globalization;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Position in the current search result set.</summary>
    private int _match;

    /// <summary>Connects the find bar to the already-mounted conversation.</summary>
    private void InitializeFind()
    {
        Label(FindButton, MessageKey.Chat.Search);
        Label(PreviousMatch, MessageKey.Chat.PreviousMatch);
        Label(NextMatch, MessageKey.Chat.NextMatch);
        Label(CloseFind, MessageKey.Common.Close);
        FindText.PlaceholderText = MessageCatalog.Get(MessageKey.Chat.Search, Language);
        FindButton.Click += (_, _) => OpenFind();
        CloseFind.Click += (_, _) => CloseFindBar();
        PreviousMatch.Click += (_, _) => SelectMatch(_match - 1);
        NextMatch.Click += (_, _) => SelectMatch(_match + 1);
        FindText.TextChanged += (_, _) => SelectMatch(0);
        FindText.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                SelectMatch(_match + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1));
                e.Handled = true;
            }
        };
    }

    /// <summary>Opens search for the active transcript.</summary>
    private void OpenFind()
    {
        WorkbenchPanel.IsVisible = false;
        ShowAnalysis(false);
        FindBar.IsVisible = true;
        FindText.Focus();
        FindText.SelectAll();
        SelectMatch(_match);
    }

    /// <summary>Returns keyboard focus to the editor.</summary>
    private void CloseFindBar()
    {
        FindBar.IsVisible = false;
        Conversation.CancelFind();
        Composer.Focus();
    }

    /// <summary>Shows the requested result and its current count.</summary>
    /// <param name="at">Wrapped match index.</param>
    private void SelectMatch(int at)
    {
        var (current, total) = Conversation.FindMatch(FindText.Text ?? string.Empty, at);
        _match = current == 0 ? 0 : current - 1;
        MatchCount.Text = string.Create(CultureInfo.InvariantCulture, $"{current}/{total}");
    }
}
