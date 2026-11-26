using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Connects the conversation cover to the existing project picker and composer.</summary>
    private void InitializeConversationHero()
    {
        StartupIntro.TryFindResource("Boot.Glyph0", out var mark);
        Conversation.ConfigureConversationHero(
            mark as Geometry,
            () => Projects.FindControl<Button>("AddProject")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),
            () =>
            {
                _workspacePreferences = _workspacePreferences with
                {
                    AcceptedDefaultWorkspace = true,
                };
                if (_connectOnOpen)
                {
                    _workspacePreferences.Save();
                }

                Conversation.SetHeroProjectChoice(false);
            },
            InsertPastedText);
    }
}
