using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Reasonix.Studio.Pages.I18n;
using System.IO;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>One workbench surface and the separate action that closes it.</summary>
internal sealed partial class WorkbenchHeaderTab : UserControl
{
    /// <summary>The optional changes count in the tab label.</summary>
    private static readonly StyledProperty<string?> s_countProperty =
        AvaloniaProperty.Register<WorkbenchHeaderTab, string?>(nameof(Count));

    /// <summary>Initializes a new instance of the <see cref="WorkbenchHeaderTab"/> class.</summary>
    /// <param name="title">Visible tab title.</param>
    /// <param name="glyph">The named icon for the surface.</param>
    /// <param name="selected">Whether this surface owns the workbench.</param>
    /// <param name="language">Interface language for the close action.</param>
    internal WorkbenchHeaderTab(string title, string glyph, bool selected, InterfaceLanguage language)
    {
        InitializeComponent();
        Picker.Content = title;
        if (glyph == "Changes")
        {
            TabGlyph.Bind(Avalonia.Controls.Shapes.Path.DataProperty, this.GetResourceObservable("Glyph.git-branch"));
        }
        else if (glyph != "File")
        {
            TabGlyph.Data = this.FindResource(glyph + "Glyph") as Geometry;
        }

        Classes.Set("active", selected);
        Picker.Classes.Set("active", selected);
        if (glyph == "New")
        {
            Dismiss.IsVisible = false;
            Picker.Focusable = false;
            Picker.IsHitTestVisible = false;
            Picker.Padding = new Avalonia.Thickness(31, 0, 12, 0);
        }
        else
        {
            AutomationProperties.SetName(Dismiss, MessageCatalog.Get("common.disable_name", language, ("name", title)));
            ToolTip.SetTip(Dismiss, MessageCatalog.Get(MessageKey.Common.Close, language));
        }
    }

    /// <summary>Gets the action that selects the represented surface.</summary>
    internal Button PickButton => Picker;

    /// <summary>Gets the action that closes the represented surface.</summary>
    internal Button CloseButton => Dismiss;

    /// <summary>Gets the count displayed for the changes tab.</summary>
    internal string? Count => GetValue(s_countProperty);

    /// <summary>Displays the count for the current changes board.</summary>
    /// <param name="board">The board that owns the authoritative count.</param>
    internal void ShowCount(GitChangesView board) => Bind(s_countProperty, board.GetObservable(GitChangesView.TabCountProperty));

    /// <summary>Applies the reference colors to the file categories marked in the tab strip.</summary>
    /// <param name="path">The canonical file path.</param>
    internal void ColorFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        TabGlyph.Classes.Set("go", extension == ".go");
        TabGlyph.Classes.Set("python", extension == ".py");
        TabGlyph.Classes.Set("sheet", extension is ".xls" or ".xlsx" or ".csv" or ".tsv");
    }

    /// <summary>Shows the unsaved dot in the close slot until the tab is hovered.</summary>
    /// <param name="dirty">Whether the represented file has unsaved edits.</param>
    internal void ShowDirty(bool dirty) => Classes.Set("dirty", dirty);
}
