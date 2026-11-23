using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Host;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Edits, records and reports the native show/hide shortcut.</summary>
internal sealed partial class GlobalShortcutView : UserControl
{
    /// <summary>The window's existing platform port.</summary>
    private readonly IHostPort _host;

    /// <summary>The current settings language.</summary>
    private readonly InterfaceLanguage _language;

    /// <summary>The most recent accepted binding and its registration status.</summary>
    private GlobalShortcutInfo _info;

    /// <summary>Initializes a new instance of the <see cref="GlobalShortcutView"/> class.</summary>
    /// <param name="language">The settings language.</param>
    /// <param name="host">The window's platform port.</param>
    /// <param name="info">The current binding and registration state.</param>
    internal GlobalShortcutView(InterfaceLanguage language, IHostPort host, GlobalShortcutInfo info)
    {
        InitializeComponent();
        _language = language;
        _host = host;
        _info = info;
        BindingLabel.Text = Text("settings.shortcut_binding");
        AutomationProperties.SetName(ShortcutInput, BindingLabel.Text);
        ShortcutInput.MaxLength = ShortcutChord.MaxLength;
        ShortcutInput.Text = info.Binding;
        Instructions.Text = Text("settings.click_record_and_press_a");
        Record.Content = Text("settings.record_shortcut");
        Apply.Content = Text("settings.apply_shortcut");
        Clear.Content = Text("settings.clear_shortcut");
        Record.Click += (_, _) =>
        {
            SetRecording(true);
            ShortcutInput.Focus();
        };
        Apply.Click += async (_, _) => await CommitAsync(ShortcutInput.Text ?? string.Empty).ConfigureAwait(true);
        Clear.Click += async (_, _) => await CommitAsync(string.Empty).ConfigureAwait(true);
        ShortcutInput.TextChanged += (_, _) => DrawDraft();
        ShortcutInput.LostFocus += (_, _) => SetRecording(false);
        ShortcutInput.AddHandler(KeyDownEvent, OnInputKey, RoutingStrategies.Tunnel);
        SizeChanged += (_, e) =>
        {
            var compact = e.NewSize.Width < 560;
            Grid.SetColumn(ActionButtons, compact ? 0 : 1);
            Grid.SetColumnSpan(ActionButtons, compact ? 2 : 1);
            Grid.SetRow(ActionButtons, compact ? 1 : 0);
            ActionsLayout.RowSpacing = compact ? 12 : 0;
            ShortcutInput.Width = Math.Max(80, Math.Min(220, e.NewSize.Width - 100));
        };
        SetRecording(false);
        DrawStatus();
        DrawDraft();
    }

    /// <summary>Gets a value indicating whether this key editor is currently recording.</summary>
    internal bool IsRecording => ShortcutInput.IsReadOnly;

    /// <summary>Formats the accelerator consistently with its platform modifiers.</summary>
    /// <param name="binding">The typed binding.</param>
    /// <returns>The display label.</returns>
    private static string Label(string binding)
    {
        var mac = OperatingSystem.IsMacOS();
        return string.Join(mac ? string.Empty : "+", binding.Split('+').Select(part => part.Trim() switch
        {
            "CommandOrControl" or "CmdOrCtrl" => mac ? "⌘" : "Ctrl",
            "Command" or "Cmd" or "Super" or "Meta" => mac ? "⌘" : "Super",
            "Control" or "Ctrl" => mac ? "⌃" : "Ctrl",
            "Alt" or "Option" => mac ? "⌥" : "Alt",
            "Shift" => mac ? "⇧" : "Shift",
            "Up" => "↑",
            "Down" => "↓",
            "Left" => "←",
            "Right" => "→",
            _ => part,
        }));
    }

    /// <summary>Consumes keys locally while recording, or submits direct input with Enter.</summary>
    /// <param name="sender">The key editor.</param>
    /// <param name="e">The recorded input.</param>
    private async void OnInputKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab)
        {
            return;
        }

        if (!IsRecording)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await CommitAsync(ShortcutInput.Text ?? string.Empty).ConfigureAwait(true);
            }

            return;
        }

        e.Handled = true;
        var chord = new ShortcutChord(e.PhysicalKey, e.KeyModifiers);
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            SetRecording(false);
        }
        else if (chord.IsBindable())
        {
            ShortcutInput.Text = chord.Format();
            SetRecording(false);
        }
    }

    /// <summary>Shows the recording state without altering the draft.</summary>
    /// <param name="recording">Whether the next key should be captured.</param>
    private void SetRecording(bool recording)
    {
        ShortcutInput.IsReadOnly = recording;
        ShortcutInput.Classes.Set("recording", recording);
        ShortcutInput.PlaceholderText = recording
            ? Text("settings.press_a_modifier_and_key")
            : Label("CommandOrControl+Shift+Space");
    }

    /// <summary>Applies the draft and displays its outcome next to the current state.</summary>
    /// <param name="binding">The edited binding, or empty to clear it.</param>
    /// <returns>The platform's registration response.</returns>
    private async Task CommitAsync(string binding)
    {
        SetRecording(false);
        IsEnabled = false;
        try
        {
            _info = await _host.SetGlobalShortcutAsync(binding).ConfigureAwait(true);
            Error.IsVisible = _info.Error.HasValue;
            if (_info.Error is { } error)
            {
                Error.Text = StatusText(error);
            }
            else
            {
                ShortcutInput.Text = _info.Binding;
            }

            DrawStatus();
            DrawDraft();
        }
        finally
        {
            IsEnabled = true;
        }
    }

    /// <summary>Reports the last accepted registration.</summary>
    private void DrawStatus() => Reading.Text = StatusText(_info.Status);

    /// <summary>Shows the draft accelerator and enables Clear when there is something to clear.</summary>
    private void DrawDraft()
    {
        var draft = ShortcutInput.Text ?? string.Empty;
        Keycap.IsVisible = draft.Length != 0;
        KeycapText.Text = Label(draft);
        Clear.IsEnabled = draft.Length != 0 || _info.Binding.Length != 0;
    }

    /// <summary>Reads the renderer's existing localized setting copy.</summary>
    /// <param name="key">The message key.</param>
    /// <returns>Localized copy.</returns>
    private string Text(string key) => MessageCatalog.Get(key, _language);

    /// <summary>Describes a registration outcome.</summary>
    /// <param name="state">The result reported by the host.</param>
    /// <returns>The localized status or error.</returns>
    private string StatusText(GlobalShortcutStatus state) => state switch
    {
        GlobalShortcutStatus.Registered => MessageCatalog.Get("settings.registered_value", _language, ("value", Label(_info.Binding))),
        GlobalShortcutStatus.Invalid => Text("settings.invalid_accelerator_use_a_modifier"),
        GlobalShortcutStatus.Unavailable => Text("settings.the_os_could_not_register"),
        GlobalShortcutStatus.SaveFailed => Text("settings.could_not_save_the_shortcut"),
        GlobalShortcutStatus.Quitting => Text("settings.studio_is_quitting_the_shortcut"),
        _ => Text("settings.global_shortcut_disabled"),
    };
}
