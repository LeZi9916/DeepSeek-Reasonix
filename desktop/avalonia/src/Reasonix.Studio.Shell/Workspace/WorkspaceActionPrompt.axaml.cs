using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Reasonix.Studio.Pages.I18n;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Workspace;

/// <summary>One reversible edit or explicit confirmation before a sidebar mutation.</summary>
public sealed partial class WorkspaceActionPrompt : UserControl
{
    /// <summary>The current explicit user decision.</summary>
    private TaskCompletionSource<string?>? _answer;

    /// <summary>Initializes a new instance of the <see cref="WorkspaceActionPrompt"/> class.</summary>
    public WorkspaceActionPrompt()
    {
        InitializeComponent();
        Cancel.Click += (_, _) => Finish(null);
        Confirm.Click += (_, _) => Finish(Value.IsVisible ? Value.Text ?? string.Empty : string.Empty);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Finish(null);
                e.Handled = true;
            }
        };
    }

    /// <summary>Waits for the actual edit or confirmation without writing before the decision.</summary>
    /// <param name="title">Operation label.</param><param name="detail">Canonical target or explanation.</param>
    /// <param name="value">Editable text, or null for confirmation.</param><param name="language">Interface language.</param>
    /// <returns>The explicit value, or null when dismissed.</returns>
    public Task<string?> Ask(string title, string detail, string? value, InterfaceLanguage language)
    {
        _answer?.TrySetResult(null);
        _answer = new TaskCompletionSource<string?>();
        Title.Text = title;
        Detail.Text = detail;
        Value.IsVisible = value is not null;
        Value.Text = value;
        Cancel.Content = MessageCatalog.Get("common.cancel", language);
        Confirm.Content = MessageCatalog.Get(value is null ? "common.confirm" : "common.save", language);
        IsVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (Value.IsVisible)
            {
                Value.Focus();
                Value.SelectAll();
            }
            else
            {
                Cancel.Focus();
            }
        });
        return _answer.Task;
    }

    /// <summary>Completes only the pending user decision.</summary>
    /// <param name="value">Accepted value, or null.</param>
    private void Finish(string? value)
    {
        IsVisible = false;
        _answer?.TrySetResult(value);
    }
}
