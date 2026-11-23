using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>A whole-number settings input with a visible unit and commit on Enter or blur.</summary>
public sealed partial class SettingsUnitField : Avalonia.Controls.UserControl
{
    /// <summary>Initializes a new instance of the <see cref="SettingsUnitField"/> class.</summary>
    public SettingsUnitField() => InitializeComponent();

    /// <summary>Sets the displayed value after reading or saving a preference.</summary>
    /// <param name="value">The canonical setting.</param>
    internal void SetValue(int value) => Number.Text = value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Displays an optional numeric setting and the automatic placeholder.</summary>
    /// <param name="value">A specific number, or null for automatic.</param>
    /// <param name="placeholder">The localized automatic label.</param>
    internal void SetOptionalValue(int? value, string placeholder)
    {
        Number.PlaceholderText = placeholder;
        Number.Text = value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>Fits the digit column to a compact numeric setting.</summary>
    /// <param name="width">Width before the unit label.</param>
    internal void SetDigitWidth(double width) => NumberLayout.ColumnDefinitions[0].Width = new GridLength(width);

    /// <summary>Connects the field to a settings action.</summary>
    /// <param name="unit">The localized unit suffix.</param>
    /// <param name="label">The accessible field name.</param>
    /// <param name="id">The stable field identifier.</param>
    /// <param name="commit">Validates or saves an edit.</param>
    internal void Open(string unit, string label, string id, Func<string, Task> commit)
    {
        Unit.Text = unit;
        AutomationProperties.SetName(Number, label);
        AutomationProperties.SetAutomationId(Number, "field:" + id);
        Number.TextChanged += (_, _) =>
        {
            var value = Number.Text ?? string.Empty;
            var digits = string.Concat(value.Where(char.IsAsciiDigit));
            if (digits != value)
            {
                var caret = Math.Min(Number.CaretIndex, digits.Length);
                Number.Text = digits;
                Number.CaretIndex = caret;
            }
        };
        Number.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await commit(Number.Text ?? string.Empty).ConfigureAwait(true);
            }
        };
        Number.LostFocus += async (_, _) => await commit(Number.Text ?? string.Empty).ConfigureAwait(true);
    }
}
