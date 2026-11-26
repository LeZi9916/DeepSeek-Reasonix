using Avalonia.Controls;
using Avalonia.Media;
using Reasonix.Studio.Pages.I18n;
using System;

namespace Reasonix.Studio.Pages;

/// <summary>The starting actions for an empty conversation.</summary>
internal sealed partial class ConversationHero : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="ConversationHero"/> class.</summary>
    internal ConversationHero() => InitializeComponent();

    /// <summary>Connects the cover to its workspace and draft owners.</summary>
    /// <param name="mark">Studio's brand geometry.</param><param name="open">Opens the project picker.</param>
    /// <param name="keep">Keeps working in the current directory.</param><param name="suggest">Inserts a prompt into the current draft.</param>
    internal void Configure(Geometry? mark, Action open, Action keep, Action<string> suggest)
    {
        Mark.Data = mark;
        OpenProject.Click += (_, _) => open();
        KeepProject.Click += (_, _) => keep();
        foreach (var button in new[] { Explain, Regression, WriteTests })
        {
            button.Click += (_, _) => suggest((string)button.Content!);
        }
    }

    /// <summary>Applies the copy and actions for the current workspace.</summary>
    /// <param name="language">Interface language.</param><param name="needsProject">Whether the initial project choice is owed.</param>
    internal void Present(InterfaceLanguage language, bool needsProject)
    {
        Title.Text = Text(needsProject ? "chat.open_a_project_first" : "chat.describe_the_task_reasonix_takes");
        Description.Text = Text(needsProject ? "chat.reading_code_running_tests_and" : "chat.it_can_read_code_research");
        OpenProject.Content = Text("chat.open_a_project");
        KeepProject.Content = Text("chat.stay_where_i_am");
        Explain.Content = Text("chat.starter_explain_the_project");
        Regression.Content = Text("chat.starter_find_the_regression");
        WriteTests.Content = Text("chat.starter_write_tests");
        Projects.IsVisible = needsProject;
        Suggestions.IsVisible = !needsProject;

        string Text(string key) => MessageCatalog.Get(key, language);
    }
}
