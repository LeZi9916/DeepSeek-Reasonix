using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Reasonix.Studio.Pages.I18n;

namespace Reasonix.Studio.Pages;

/// <summary>A selectable user bubble with a copy action and the reference reading measure.</summary>
internal sealed partial class UserMessage : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="UserMessage"/> class.</summary>
    /// <param name="text">Original user message.</param>
    /// <param name="language">Interface language.</param>
    internal UserMessage(string text, InterfaceLanguage language)
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<SelectableTextBlock>("Message")!.Text = text;
        var copy = this.FindControl<Button>("Copy")!;
        var label = MessageCatalog.Get(MessageKey.Common.Copy, language);
        Avalonia.Automation.AutomationProperties.SetName(copy, label);
        ToolTip.SetTip(copy, label);
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text).ConfigureAwait(true);
            }
        };
        SizeChanged += (_, _) => this.FindControl<StackPanel>("BubbleColumn")!.MaxWidth = Bounds.Width * 0.7;
    }
}
