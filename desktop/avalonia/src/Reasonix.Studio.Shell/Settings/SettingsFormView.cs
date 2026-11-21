using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>A settings form whose labels use the shell's translation catalog.</summary>
public class SettingsFormView : UserControl
{
    /// <summary>Gets the reader's selected language.</summary>
    protected InterfaceLanguage Language
    {
        get; private set;
    }

    /// <summary>Creates secondary copy for a data-dependent row.</summary>
    /// <param name="value">Text to display.</param>
    /// <returns>The wrapping text.</returns>
    protected static TextBlock Caption(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = 12 };

    /// <summary>Applies the translations named by the form's XAML controls.</summary>
    /// <param name="language">The reader's language.</param>
    protected void Translate(InterfaceLanguage language)
    {
        Language = language;
        foreach (var control in this.GetLogicalDescendants().OfType<Control>())
        {
            if (control.Tag is not string key)
            {
                continue;
            }

            var label = Text(key);
            switch (control)
            {
                case TextBlock block:
                    block.Text = label;
                    if (block.Parent is StackPanel field && field.Children.Count == 2 && field.Children[1] is TextBox or ComboBox)
                    {
                        AutomationProperties.SetLabeledBy(field.Children[1], block);
                    }

                    break;
                case Button button:
                    button.Content = label;
                    break;
                case TextBox input:
                    input.PlaceholderText = label;
                    break;
            }

            AutomationProperties.SetName(control, label);
        }
    }

    /// <summary>Translates a label or status.</summary>
    /// <param name="key">Catalog key.</param>
    /// <param name="values">Named arguments.</param>
    /// <returns>The visible text.</returns>
    protected string Text(string key, params (string Name, string Value)[] values) => MessageCatalog.Get(key, Language, values);

    /// <summary>A canonical value with its localized display label.</summary>
    /// <param name="Id">Stored value.</param>
    /// <param name="Label">Visible name.</param>
    protected sealed record Option(string Id, string Label)
    {
        /// <inheritdoc/>
        public override string ToString() => Label;
    }
}
