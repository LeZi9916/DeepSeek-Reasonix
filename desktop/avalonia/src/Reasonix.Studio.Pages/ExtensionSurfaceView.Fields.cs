using Avalonia.Controls;
using Avalonia.Layout;
using Reasonix.Studio.Contracts.Wire;
using System;
using System.Linq;
using System.Text.Json.Nodes;

namespace Reasonix.Studio.Pages;

internal sealed partial class ExtensionSurfaceView
{
    /// <summary>Recognizes the reference's missing required values.</summary>
    /// <param name="value">A proposed field value.</param><returns>Whether the field remains unfilled.</returns>
    private static bool IsEmpty(JsonNode? value) => value switch
    {
        null => true,
        JsonArray array => array.Count == 0,
        JsonValue scalar when scalar.TryGetValue<string>(out var text) => string.IsNullOrWhiteSpace(text),
        JsonValue scalar when scalar.TryGetValue<bool>(out var confirm) => !confirm,
        _ => false,
    };

    /// <summary>Creates the editor declared by an extension form.</summary>
    /// <param name="field">The field schema.</param><param name="value">The draft value.</param><param name="change">Stores edits independently of the mounted row.</param>
    /// <returns>The input control.</returns>
    private static Control Field(ExtensionFormField field, JsonNode? value, Action<JsonNode?> change)
    {
        switch (field.Kind)
        {
            case "confirm":
                var check = new CheckBox { Content = First(field.Label, field.Key), IsChecked = value is JsonValue flag && flag.TryGetValue<bool>(out var selected) && selected };
                check.IsCheckedChanged += (_, _) => change(JsonValue.Create(check.IsChecked == true));
                return check;
            case "select":
                var select = new ComboBox { ItemsSource = field.Options, SelectedItem = value?.ToString() };
                select.SelectionChanged += (_, _) => change(JsonValue.Create(select.SelectedItem as string));
                return select;
            case "multiselect":
                var choices = field.Options ?? [];
                var picked = (value as JsonArray ?? []).Select(option => option?.ToString()).ToHashSet(StringComparer.Ordinal);
                var list = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
                foreach (var option in choices)
                {
                    var item = new CheckBox { Content = option, IsChecked = picked.Contains(option) };
                    item.IsCheckedChanged += (_, _) =>
                    {
                        if (item.IsChecked == true)
                        {
                            picked.Add(option);
                        }
                        else
                        {
                            picked.Remove(option);
                        }

                        change(new JsonArray(choices.Where(picked.Contains).Select(option => (JsonNode?)JsonValue.Create(option)).ToArray()));
                    };
                    list.Children.Add(item);
                }

                return list;
            default:
                var text = new TextBox { Text = value?.ToString() ?? string.Empty };
                text.TextChanged += (_, _) => change(JsonValue.Create(text.Text ?? string.Empty));
                return text;
        }
    }
}
