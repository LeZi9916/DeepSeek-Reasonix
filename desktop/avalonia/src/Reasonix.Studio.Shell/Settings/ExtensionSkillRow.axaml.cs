using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>A standalone skill with source badges and the reference metadata disclosure.</summary>
internal sealed partial class ExtensionSkillRow : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="ExtensionSkillRow"/> class.</summary>
    public ExtensionSkillRow() => InitializeComponent();

    /// <summary>Projects skill data, copy, switch and removal actions.</summary>
    /// <param name="skill">The authoritative skill inventory record.</param>
    /// <param name="scope">Localized layer name.</param>
    /// <param name="language">Settings language.</param>
    /// <param name="setEnabled">Writes the effective state in the current project.</param>
    /// <param name="remove">Removes the selected local source after confirmation.</param>
    internal void Present(JsonObject skill, string scope, InterfaceLanguage language, Func<bool, Task<bool>> setEnabled, Func<Task>? remove)
    {
        var id = (string?)skill["name"] ?? string.Empty;
        Tag = id;
        var command = (string?)skill["slashName"] is { Length: > 0 } slash ? "/" + slash : null;
        Title.Text = command ?? id;
        Description.Text = (string?)skill["description"] ?? Text("ext.no_description_written");
        Scope.Text = scope;
        Subagent.IsVisible = (bool?)skill["subagent"] == true;
        ReadOnly.IsVisible = (bool?)skill["readOnly"] == true;
        AgentLabel.Text = Text("common.subagents");
        ReadLabel.Text = Text("common.read_only");
        Copy.IsVisible = command is not null;
        if (command is not null)
        {
            AutomationProperties.SetName(Copy, MessageCatalog.Get("ext.copy_invocation_command", language, ("command", command)));
            Copy.Click += async (_, _) =>
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                {
                    await clipboard.SetTextAsync(command).ConfigureAwait(true);
                }
            };
        }

        var enabled = (bool?)skill["enabled"] == true;
        Toggle.IsChecked = enabled;
        Toggle.Tag = id;
        AutomationProperties.SetName(Toggle, id);
        Toggle.Click += async (_, _) =>
        {
            Toggle.IsEnabled = false;
            try
            {
                var next = Toggle.IsChecked == true;
                if (await setEnabled(next).ConfigureAwait(true))
                {
                    enabled = next;
                }

                Toggle.IsChecked = enabled;
            }
            finally
            {
                Toggle.IsEnabled = true;
            }
        };

        More.IsVisible = remove is not null;
        More.Click += (_, _) => RemovePanel.IsVisible = !RemovePanel.IsVisible;
        Cancel.Content = Text("common.cancel");
        Remove.Content = Text("common.delete");
        RemoveHint.Text = Text("ext.remove_2") + " " + id;
        Cancel.Click += (_, _) => RemovePanel.IsVisible = false;
        Remove.Click += async (_, _) =>
        {
            if (remove is not null)
            {
                Remove.IsEnabled = false;
                try
                {
                    await remove().ConfigureAwait(true);
                }
                finally
                {
                    Remove.IsEnabled = true;
                }
            }
        };

        var declarations = (bool?)skill["subagent"] == true
            && (skill["model"] is not null || skill["effort"] is not null || skill["allowedTools"] is JsonArray { Count: > 0 });
        if (declarations)
        {
            Inspect.Content = Text("ext.subagent_declarations");
            AddFact(Text("ext.declared_model"), (string?)skill["model"]);
            AddFact(Text("ext.declared_reasoning_effort"), (string?)skill["effort"]);
            AddFact(Text("ext.declared_tools"), string.Join(" · ", skill["allowedTools"]?.AsArray().Select(item => (string?)item) ?? []));
        }
        else
        {
            Inspect.Content = Text("ext.skill_information");
            AddFact(Text("ext.license"), (string?)skill["license"]);
            AddFact(Text("ext.requirements"), (string?)skill["compatibility"]);
            if (skill["metadata"] is JsonObject metadata)
            {
                foreach (var (key, value) in metadata)
                {
                    AddFact(key, value?.ToString());
                }
            }
        }

        Inspect.IsVisible = Details.Children.Count > 0;
        Inspect.Tag = id;
        Inspect.Click += (_, _) => Details.IsVisible = !Details.IsVisible;

        string Text(string key) => MessageCatalog.Get(key, language);

        void AddFact(string caption, string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            var name = new TextBlock { Text = caption, Classes = { "skill-label" }, VerticalAlignment = VerticalAlignment.Top };
            var data = new TextBlock { Text = value, Classes = { "skill-label" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            data.Bind(TextBlock.FontFamilyProperty, data.GetResourceObservable("WorkbenchMonoFamily"));
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("60,*"), Children = { name, data } };
            Grid.SetColumn(data, 1);
            Details.Children.Add(row);
        }
    }
}
