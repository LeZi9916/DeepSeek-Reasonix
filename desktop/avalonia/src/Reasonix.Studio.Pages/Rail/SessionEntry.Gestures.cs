using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;

namespace Reasonix.Studio.Pages.Rail;

internal sealed partial class SessionEntry
{
    /// <summary>Gets the canonical transcript represented by this row.</summary>
    internal string SessionPath
    {
        get;
    }

    /// <summary>Gets a value indicating whether the row currently owns a title draft.</summary>
    internal bool IsRenaming => Rename.IsVisible;

    /// <summary>Retains an unfinished title across a canonical tree refresh without submitting it.</summary>
    /// <returns>The title being edited.</returns>
    internal string SuspendRename()
    {
        var text = Rename.Text ?? string.Empty;
        Rename.IsVisible = false;
        return text;
    }

    /// <summary>Restores a retained title draft after its canonical row has been rebuilt.</summary>
    /// <param name="text">Unsubmitted title.</param>
    internal void RestoreRename(string text)
    {
        Rename.Text = text;
        Rename.IsVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (TopLevel.GetTopLevel(this) is not null && Rename.IsVisible)
            {
                Rename.Focus();
                Rename.CaretIndex = Rename.Text?.Length ?? 0;
            }
        });
    }

    /// <summary>Connects row gestures to the same enabled operations as its menu.</summary>
    /// <param name="menu">Canonical enabled actions.</param><param name="language">Interface language.</param>
    /// <param name="selecting">Whether selection owns row input.</param><param name="rename">Commits an inline title.</param>
    private void InitializeGestures(MenuFlyout menu, InterfaceLanguage language, bool selecting, Action<string>? rename)
    {
        AutomationProperties.SetName(Rename, MessageCatalog.Get("common.rename_this_session", language));
        Rename.PlaceholderText = MessageCatalog.Get("common.chat_title", language);
        var original = Title.Text ?? string.Empty;
        Open.AddHandler(
            PointerPressedEvent,
            (_, e) =>
        {
            if (selecting || rename is null || !e.GetCurrentPoint(Open).Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (e.ClickCount < 2 || e.ClickCount % 2 != 0)
            {
                return;
            }

            e.Handled = true;
            Rename.Text = original;
            Rename.IsVisible = true;
            Rename.Focus();
            Rename.SelectAll();
        },
            RoutingStrategies.Tunnel);
        Rename.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                e.Handled = true;
                FinishRename(e.Key == Key.Enter);
                Open.Focus();
            }
        };
        Rename.LostFocus += (_, _) => FinishRename(true);
        Open.AddHandler(
            KeyDownEvent,
            (_, e) =>
        {
            if (selecting || Rename.IsVisible || !ReferenceEquals(e.Source, Open))
            {
                return;
            }

            var delete = e.KeyModifiers.HasFlag(KeyModifiers.Alt) || e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                ? false
                : (e.Key == Key.Delete && !e.KeyModifiers.HasFlag(KeyModifiers.Meta))
                    || (OperatingSystem.IsMacOS() && e.Key == Key.Back && e.KeyModifiers.HasFlag(KeyModifiers.Meta));
            var action = e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key is Key.Up or Key.Down
                ? e.Key == Key.Up ? "session.move-up" : "session.move-down"
                : delete ? "session.delete" : string.Empty;
            if (action.Length == 0)
            {
                return;
            }

            e.Handled = true;
            var item = menu.Items.OfType<MenuItem>().FirstOrDefault(item => AutomationProperties.GetAutomationId(item) == "action:" + action);
            if (item?.IsEnabled == true)
            {
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
        },
            RoutingStrategies.Tunnel);

        void FinishRename(bool commit)
        {
            if (!Rename.IsVisible)
            {
                return;
            }

            var next = Rename.Text?.Trim() ?? string.Empty;
            Rename.IsVisible = false;
            if (commit && next.Length > 0 && next != original.Trim())
            {
                rename?.Invoke(next);
            }
        }
    }
}
