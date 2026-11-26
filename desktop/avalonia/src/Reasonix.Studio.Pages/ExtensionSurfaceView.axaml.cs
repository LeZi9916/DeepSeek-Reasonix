using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Pages;

/// <summary>Renders the content and user actions of a kernel-issued extension surface.</summary>
internal sealed partial class ExtensionSurfaceView : UserControl
{
    /// <summary>The current interface language.</summary>
    private readonly InterfaceLanguage _language;

    /// <summary>Initializes a new instance of the <see cref="ExtensionSurfaceView"/> class.</summary>
    /// <param name="surface">Canonical surface.</param><param name="draft">The retained form state.</param>
    /// <param name="language">The active interface language.</param><param name="invoke">Invokes a declared extension action.</param>
    /// <param name="submit">Submits the declared form's values.</param>
    internal ExtensionSurfaceView(ExtensionSurface surface, ExtensionSurfaceDraft draft, InterfaceLanguage language, Func<string, Task<bool>>? invoke, Func<ExtensionSurface, JsonObject, Task<bool>>? submit)
    {
        InitializeComponent();
        _language = language;
        var plugin = string.IsNullOrEmpty(surface.PluginID) ? surface.SurfaceID : surface.PluginID;
        Identity.Text = MessageCatalog.Get("chat.extension_id", language, ("id", plugin));
        if (!string.IsNullOrEmpty(surface.Code))
        {
            Title.Text = Text("chat.extension_surface_expired");
            return;
        }

        var card = surface.Card;
        var form = surface.Form;
        var title = First(card?.Title, form?.Title, surface.Notification?.Title, surface.Status?.Label);
        Title.Text = title;
        Title.IsVisible = title.Length > 0;
        if (surface.View is { } view)
        {
            foreach (var node in view.Body ?? [])
            {
                Details.Children.Add(Node(node, AddAction));
            }
        }

        if (surface.Status is { } status)
        {
            AddReading(Details, status.Label == Title.Text ? null : status.Label, status.Detail, status.Progress);
        }

        if (surface.Notification is { } notice)
        {
            AddReading(Details, notice.Title == Title.Text ? null : notice.Title, notice.Body, null);
        }

        if (card is not null)
        {
            AddReading(Details, null, First(card.Markdown, card.Text), card.Progress, markdown: !string.IsNullOrEmpty(card.Markdown));
            Fields.ItemsSource = card.Fields;
            foreach (var action in card.Actions ?? [])
            {
                Actions.Children.Add(AddAction(action.ActionID, action.Label));
            }
        }

        if (form is not null)
        {
            PresentForm(surface, draft, submit);
        }

        Button AddAction(string? id, string? label)
        {
            var action = new Button { Content = First(label, id), IsEnabled = invoke is not null && !string.IsNullOrEmpty(id) };
            AutomationProperties.SetAutomationId(action, "action:extensions.invoke");
            action.Click += async (_, _) =>
            {
                if (invoke is null || string.IsNullOrEmpty(id) || !action.IsEnabled)
                {
                    return;
                }

                action.IsEnabled = false;
                try
                {
                    await invoke($"/{surface.PluginID}:{id}").ConfigureAwait(true);
                }
                finally
                {
                    action.IsEnabled = true;
                }
            };
            return action;
        }
    }

    /// <summary>Returns the first nonempty string among the alternative surface labels.</summary>
    /// <param name="text">The possible labels.</param><returns>The chosen label.</returns>
    private static string First(params string?[] text) => text.FirstOrDefault(value => !string.IsNullOrEmpty(value)) ?? string.Empty;

    /// <summary>Creates a selectable reading in the current surface typography.</summary>
    /// <param name="text">The extension's content.</param><param name="markdown">Whether to render Markdown.</param>
    /// <returns>The reading control.</returns>
    private static SelectableTextBlock Reading(string text, bool markdown = false)
    {
        var reading = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, [!TextBlock.FontSizeProperty] = StudioTypography.Size(13) };
        if (markdown)
        {
            MarkdownAnswer.Show(reading, text);
        }
        else
        {
            reading.Text = text;
        }

        return reading;
    }

    /// <summary>Draws a label, selectable content and optional progress in order.</summary>
    /// <param name="parent">The owning stack.</param><param name="label">Optional label.</param><param name="body">Optional content.</param>
    /// <param name="progress">Completion fraction.</param><param name="markdown">Whether the body is Markdown.</param>
    private static void AddReading(StackPanel parent, string? label, string? body, double? progress, bool markdown = false)
    {
        if (!string.IsNullOrEmpty(label))
        {
            var heading = Reading(label);
            heading.FontWeight = FontWeight.Medium;
            parent.Children.Add(heading);
        }

        if (!string.IsNullOrEmpty(body))
        {
            parent.Children.Add(Reading(body, markdown));
        }

        if (progress is { } value && double.IsFinite(value))
        {
            parent.Children.Add(new ProgressBar { Minimum = 0, Maximum = 1, Value = Math.Clamp(value, 0, 1), Height = 5 });
        }
    }

    /// <summary>Renders the supported declarative extension nodes.</summary>
    /// <param name="node">The canonical node.</param><param name="action">Creates a declared action button.</param>
    /// <returns>The native node presentation.</returns>
    private static Control Node(ExtensionViewNode node, Func<string?, string?, Button> action)
    {
        if (node.Kind == "button")
        {
            return action(node.ActionID, node.Label);
        }

        var content = new StackPanel { Spacing = 6, Orientation = node.Kind == "row" ? Avalonia.Layout.Orientation.Horizontal : Avalonia.Layout.Orientation.Vertical };
        AddReading(content, First(node.Key, node.Label), node.Value, node.Progress, markdown: node.Kind == "markdown");
        foreach (var child in node.Children ?? [])
        {
            content.Children.Add(Node(child, action));
        }

        return content;
    }

    /// <summary>Builds the form and gates input on the kernel acknowledgement.</summary>
    /// <param name="surface">The surface issuing the form.</param><param name="draft">The retained values.</param>
    /// <param name="submit">The available acknowledgement path.</param>
    private void PresentForm(ExtensionSurface surface, ExtensionSurfaceDraft draft, Func<ExtensionSurface, JsonObject, Task<bool>>? submit)
    {
        Form.IsVisible = true;
        var form = surface.Form;
        Inputs.Children.Add(Reading(MessageCatalog.Get("chat.this_is_a_question_from", _language, ("id", surface.PluginID))));
        AddReading(Inputs, null, form.Message, null);
        foreach (var field in form.Fields ?? [])
        {
            if (!draft.Values.ContainsKey(field.Key))
            {
                if (field.Default.ValueKind != JsonValueKind.Undefined)
                {
                    draft.Values[field.Key] = JsonNode.Parse(field.Default.GetRawText());
                }
                else if (field.Kind == "confirm")
                {
                    draft.Values[field.Key] = JsonValue.Create(false);
                }
                else if (field.Kind == "multiselect")
                {
                    draft.Values[field.Key] = new JsonArray();
                }
            }

            var label = First(field.Label, field.Key);
            Inputs.Children.Add(Reading(field.Required ? label + " *" : label));
            var editor = Field(field, draft.Values.GetValueOrDefault(field.Key), value =>
            {
                draft.Values[field.Key] = value;
                Refresh();
            });
            AutomationProperties.SetName(editor, label);
            Inputs.Children.Add(editor);
        }

        AttachedToVisualTree += (_, _) =>
        {
            draft.Changed += Refresh;
            Refresh();
        };
        DetachedFromVisualTree += (_, _) => draft.Changed -= Refresh;
        Submit.Click += async (_, _) =>
        {
            if (submit is null || draft.Submission != ExtensionSubmission.Editable || Missing() != 0)
            {
                return;
            }

            draft.Submission = ExtensionSubmission.Pending;
            draft.Notify();
            var values = new JsonObject();
            foreach (var field in form.Fields ?? [])
            {
                if (draft.Values.TryGetValue(field.Key, out var value))
                {
                    values[field.Key] = value?.DeepClone();
                }
            }

            var accepted = false;
            try
            {
                accepted = await submit(surface, values).ConfigureAwait(true);
            }
            finally
            {
                draft.Submission = accepted ? ExtensionSubmission.Accepted : ExtensionSubmission.Editable;
                draft.Notify();
            }
        };
        Refresh();

        int Missing() => form.Fields?.Count(field => field.Required && IsEmpty(draft.Values.GetValueOrDefault(field.Key))) ?? 0;

        void Refresh()
        {
            var missing = Missing();
            Inputs.IsEnabled = draft.Submission == ExtensionSubmission.Editable;
            Submit.IsVisible = draft.Submission != ExtensionSubmission.Accepted;
            Submit.IsEnabled = Inputs.IsEnabled && missing == 0 && submit is not null;
            var ready = missing > 0 ? MessageCatalog.Get("chat.submit_n_still_to_fill", _language, ("n", Format.Count(missing, _language))) : Text("chat.submit");
            Submit.Content = draft.Submission == ExtensionSubmission.Pending ? Text("common.submitting") : ready;
        }
    }

    /// <summary>Reads a key in the current interface language.</summary>
    /// <param name="key">The catalog identity.</param><returns>The localized text.</returns>
    private string Text(string key) => MessageCatalog.Get(key, _language);
}
