using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Edits the system persona used by new conversations.</summary>
public sealed partial class SystemPersonaView : SettingsFormView
{
    /// <summary>The last saved persona on this device.</summary>
    private PersonaDraft _saved = new(string.Empty, string.Empty, true);

    /// <summary>Initializes a new instance of the <see cref="SystemPersonaView"/> class.</summary>
    public SystemPersonaView() => InitializeComponent();

    /// <summary>Gets a value indicating whether the reader has edited any persona fields.</summary>
    internal bool HasChanges => Draft != _saved;

    /// <summary>Gets the fields currently shown in the editor.</summary>
    private PersonaDraft Draft => new(Prefix.Text ?? string.Empty, Suffix.Text ?? string.Empty, OmitRuntime.IsChecked != true);

    /// <summary>Connects the editor to the persona settings endpoint.</summary>
    /// <param name="api">The active kernel.</param>
    /// <param name="persona">The last saved settings and example profiles.</param>
    /// <param name="language">The shell language.</param>
    /// <param name="back">Closes the editor through the settings guard.</param>
    /// <param name="describe">Renders an error in the active language.</param>
    /// <param name="token">The window lifetime.</param>
    internal void Open(StudioPageClient api, JsonObject persona, InterfaceLanguage language, Func<Task> back, Func<Exception, string> describe, CancellationToken token)
    {
        Translate(language);
        AutomationProperties.SetName(Back, Text("persona.back_to_presets"));
        var profiles = new List<(PersonaDraft Draft, Button Button, string About)>();
        _saved = Read(persona);
        var busy = false;
        foreach (var preset in persona["presets"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            var id = (string?)preset["id"] ?? string.Empty;
            var (name, about) = id switch
            {
                "engineer" => ("persona.rigorous_engineer", "persona.verify_before_concluding_make_the"),
                "concise" => ("persona.concise", "persona.answer_briefly_and_directly_skip"),
                "mentor" => ("persona.mentor", "persona.explain_trade_offs_and_reasoning"),
                "critic" => ("persona.critic", "persona.review_adversarially_name_the_failure"),
                _ => (id, string.Empty),
            };
            var draft = Read(preset);
            var button = new Button
            {
                Content = Text(name),
                Tag = id,
                Classes = { "preset-flat", "preset-muted", "persona-chip" },
            };
            AutomationProperties.SetAutomationId(button, "action:persona.preset");
            button.Click += (_, _) => Apply(draft);
            Choices.Children.Add(button);
            profiles.Add((draft, button, Text(about)));
        }

        Profiles.IsVisible = ProfileDescription.IsVisible = profiles.Count > 0;
        Apply(_saved);
        Back.Click += async (_, _) => await back().ConfigureAwait(true);
        BackFooter.Click += async (_, _) => await back().ConfigureAwait(true);
        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                await back().ConfigureAwait(true);
            }
        };
        Prefix.TextChanged += (_, _) => Refresh();
        Suffix.TextChanged += (_, _) => Refresh();
        OmitRuntime.IsCheckedChanged += (_, _) => Refresh();
        Save.Click += async (_, _) =>
        {
            if (busy || !HasChanges)
            {
                return;
            }

            var chosen = Draft;
            busy = true;
            IsEnabled = false;
            try
            {
                await api.SetPersonaAsync(chosen.Prefix, chosen.Suffix, chosen.Runtime, token).ConfigureAwait(true);
                _saved = chosen;
                await back().ConfigureAwait(true);
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    Error.Text = describe(error);
                    Error.IsVisible = true;
                }
            }
            finally
            {
                busy = false;
                IsEnabled = true;
                Save.IsEnabled = HasChanges;
            }
        };

        void Apply(PersonaDraft draft)
        {
            Prefix.Text = draft.Prefix;
            Suffix.Text = draft.Suffix;
            OmitRuntime.IsChecked = !draft.Runtime;
            Refresh();
        }

        void Refresh()
        {
            var draft = Draft;
            var selected = profiles.FirstOrDefault(profile => profile.Draft == draft);
            foreach (var profile in profiles)
            {
                profile.Button.Classes.Set("active", profile.Button == selected.Button);
            }

            var blank = draft.Prefix.Length == 0 && draft.Suffix.Length == 0;
            CurrentProfile.Text = selected.Button?.Content as string ?? (blank ? string.Empty : Text("common.custom"));
            ProfileDescription.Text = selected.About ?? Text(blank ? "persona.no_preset_applied_fill_in" : "persona.the_preset_text_has_been");
            Error.IsVisible = false;
            Save.IsEnabled = !busy && HasChanges;
        }
    }

    /// <summary>Reads an example profile or the active preference.</summary>
    /// <param name="value">The kernel response object.</param>
    /// <returns>The usable fields.</returns>
    private static PersonaDraft Read(JsonObject value) => new((string?)value["prefix"] ?? string.Empty, (string?)value["suffix"] ?? string.Empty, (bool?)value["include_runtime_context"] == true);

    /// <summary>The three editable system persona fields.</summary>
    /// <param name="Prefix">Instructions preceding the built-in prompt.</param>
    /// <param name="Suffix">Instructions following the built-in prompt.</param>
    /// <param name="Runtime">Whether runtime context is included.</param>
    private sealed record PersonaDraft(string Prefix, string Suffix, bool Runtime);
}
