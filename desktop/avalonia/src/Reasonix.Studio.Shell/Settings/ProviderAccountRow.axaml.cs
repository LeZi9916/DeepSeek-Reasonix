using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>A selectable service with inline actions for renaming and ordering.</summary>
internal sealed partial class ProviderAccountRow : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="ProviderAccountRow"/> class.</summary>
    public ProviderAccountRow() => InitializeComponent();

    /// <summary>Displays the account identity and status read from the kernel.</summary>
    /// <param name="account">One endpoint and credential slot.</param>
    /// <param name="language">The settings language.</param>
    /// <param name="moves">Whether the account can be moved up or down.</param>
    internal void Show(ProviderServiceAccount account, InterfaceLanguage language, (bool Up, bool Down) moves)
    {
        ServiceName.Text = account.Label;
        ServiceHost.Text = account.Host;
        AutomationProperties.SetName(SelectAccount, account.Label);
        ToolTip.SetTip(ServiceHost, account.Host);
        var entries = account.Entries.Values;
        var state = entries.Any(entry => (bool?)entry["lastCheck"]?["ok"] == false)
            ? "fail"
            : account.InUse
                ? "use"
                : entries.All(entry => (bool?)entry["hasKey"] != true)
                    ? "warn"
                    : entries.Any(entry => (bool?)entry["lastCheck"]?["ok"] == true) ? "pass" : string.Empty;
        if (state.Length > 0)
        {
            HealthDot.Classes.Add(state);
            var status = state switch
            {
                "fail" => "models.last_test_failed",
                "warn" => "models.no_key",
                "pass" => "models.last_test_passed",
                _ => "models.in_use",
            };
            ToolTip.SetTip(HealthDot, MessageCatalog.Get(status, language));
        }

        DrawMark(account);
        Up.IsVisible = moves.Up;
        Down.IsVisible = moves.Down;
        NameButton(Rename, "models.rename_name_f2");
        NameButton(Up, "models.move_name_up_alt_up");
        NameButton(Down, "models.move_name_down_alt_down");
        AutomationProperties.SetName(RenameInput, MessageCatalog.Get("models.rename_name", language, ("name", account.Label)));

        void NameButton(Button button, string key)
        {
            var caption = MessageCatalog.Get(key, language, ("name", account.Label));
            AutomationProperties.SetName(button, caption);
            ToolTip.SetTip(button, caption);
        }
    }

    /// <summary>Shows whether the detail pane belongs to this account.</summary>
    /// <param name="selected">The displayed account.</param>
    internal void SetSelected(bool selected) => Frame.Classes.Set("active", selected);

    /// <summary>Returns keyboard focus to this account after reordering.</summary>
    internal void FocusAccount() => SelectAccount.Focus();

    /// <summary>Connects each action to the settings view.</summary>
    /// <param name="select">Selects this account if the editor can leave.</param>
    /// <param name="rename">Starts renaming the account.</param>
    /// <param name="move">Moves the account in the list.</param>
    internal void OnActions(Action select, Action rename, Action<int> move)
    {
        SelectAccount.Click += (_, _) => select();
        Rename.Click += (_, _) => rename();
        Up.Click += (_, _) => move(-1);
        Down.Click += (_, _) => move(1);
        SelectAccount.KeyDown += (_, e) =>
        {
            if (e.Key == Key.F2)
            {
                e.Handled = true;
                rename();
            }
            else if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Up or Key.Down)
            {
                e.Handled = true;
                move(e.Key == Key.Up ? -1 : 1);
            }
        };
    }

    /// <summary>Edits the display name in the selected row until focus leaves.</summary>
    /// <param name="initial">Name shown before editing.</param>
    /// <param name="placeholder">Derived label when the name is erased.</param>
    /// <param name="commit">Writes a changed display name.</param>
    internal void BeginRename(string initial, string placeholder, Func<string, Task> commit)
    {
        if (RenameInput.IsVisible)
        {
            RenameInput.Focus();
            return;
        }

        RenameInput.PlaceholderText = placeholder;
        RenameInput.Text = initial;
        Frame.Classes.Add("renaming");
        Height = 46;
        RenameInput.IsVisible = true;
        RenameInput.KeyDown += Finish;
        RenameInput.LostFocus += Commit;
        RenameInput.Focus();
        RenameInput.SelectAll();

        void Finish(object? sender, KeyEventArgs e)
        {
            if (e.Key is not (Key.Enter or Key.Escape))
            {
                return;
            }

            e.Handled = true;
            if (e.Key == Key.Escape)
            {
                RenameInput.Text = initial;
            }

            SelectAccount.Focus();
        }

        async void Commit(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            RenameInput.KeyDown -= Finish;
            RenameInput.LostFocus -= Commit;
            RenameInput.IsVisible = false;
            Height = 49;
            Frame.Classes.Remove("renaming");
            var value = RenameInput.Text?.Trim() ?? string.Empty;
            if (value != initial)
            {
                await commit(value).ConfigureAwait(true);
            }
        }
    }

    /// <summary>Draws the reference vendor artwork or its service fallback.</summary>
    /// <param name="account">The endpoint's declared mark and identity.</param>
    private void DrawMark(ProviderServiceAccount account)
    {
        if (ProviderVendorMark.Create(this, account.Mark, 20) is { } logo)
        {
            MarkHost.Children.Clear();
            MarkHost.Children.Add(logo);
            return;
        }

        var hash = 0u;
        foreach (var ch in account.Key)
        {
            hash = (hash * 31) + ch;
        }

        var tone = "Cat" + ((hash % 5) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        MarkGlyph.Data = (Geometry)this.FindResource("Mark.server")!;
        MarkGlyph.Bind(Avalonia.Controls.Shapes.Path.StrokeProperty, MarkGlyph.GetResourceObservable(tone + "Brush"));
        MarkWash.Bind(Border.BackgroundProperty, MarkWash.GetResourceObservable(tone + "Brush"));
        MarkScale.Width = MarkScale.Height = 16;
    }
}
