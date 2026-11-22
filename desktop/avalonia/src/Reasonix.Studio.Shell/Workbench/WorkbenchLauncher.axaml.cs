using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.IO;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>The workbench's native files, browser, terminal and recent-file launcher.</summary>
public sealed partial class WorkbenchLauncher : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="WorkbenchLauncher"/> class.</summary>
    public WorkbenchLauncher()
    {
        InitializeComponent();
        Files.Click += (_, _) => FilesRequested?.Invoke();
        Browser.Click += (_, _) => BrowserRequested?.Invoke();
        Terminal.Click += (_, _) => TerminalRequested?.Invoke();
    }

    /// <summary>Raised to open the existing explorer.</summary>
    public event Action? FilesRequested;

    /// <summary>Raised to create a browser surface.</summary>
    public event Action? BrowserRequested;

    /// <summary>Raised to create a terminal surface.</summary>
    public event Action? TerminalRequested;

    /// <summary>Raised to reopen a canonical recently viewed path.</summary>
    public event Action<string>? FileRequested;

    /// <summary>Projects the active workspace's entry points and recent paths.</summary>
    /// <param name="changes">Measured changed-file count.</param><param name="recent">Recently opened paths.</param>
    /// <param name="language">Interface language.</param>
    public void Show(int changes, IEnumerable<string> recent, InterfaceLanguage language)
    {
        string Say(string key) => MessageCatalog.Get(key, language);
        Title.Text = Say("workbench.open_in_the_workbench");
        FilesTitle.Text = Say("workbench.explorer");
        FilesHint.Text = Say("workbench.changes_history_and_files");
        Changes.Text = changes > 0 ? changes.ToString(System.Globalization.CultureInfo.CurrentCulture) : string.Empty;
        BrowserTitle.Text = Say("workbench.browser");
        BrowserHint.Text = Say("workbench.view_web_pages_beside_the");
        TerminalTitle.Text = Say("terminal.name");
        TerminalHint.Text = Say("terminal.launch_hint");
        RecentTitle.Text = Say("workbench.recently_opened");
        Recent.Children.Clear();
        foreach (var path in recent)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(new TextBlock { Text = Path.GetFileName(path) });
            var directory = (Path.GetDirectoryName(path) ?? string.Empty).Replace('\\', '/');
            if (directory.Length > 0)
            {
                var hint = new TextBlock { Text = directory + "/" };
                hint.Classes.Add("launcher-dir");
                content.Children.Add(hint);
            }

            var item = new Button { Content = content, Classes = { "launcher" }, MinHeight = 28, Height = 28, Padding = new Avalonia.Thickness(8, 2) };
            AutomationProperties.SetAutomationId(item, "action:workbench.launch-recent");
            ToolTip.SetTip(item, path);
            item.Click += (_, _) => FileRequested?.Invoke(path);
            Recent.Children.Add(item);
        }

        RecentTitle.IsVisible = Recent.Children.Count > 0;
    }
}
