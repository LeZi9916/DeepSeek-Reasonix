using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages.I18n;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>Displays a parsed delimited file in the workbench's virtualized table.</summary>
internal sealed partial class DelimitedDocumentPreview : UserControl
{
    /// <summary>The file retaining this table's reading position.</summary>
    private readonly FileBuffer _file;

    /// <summary>Initializes a new instance of the <see cref="DelimitedDocumentPreview"/> class.</summary>
    /// <param name="file">The selected file and its retained position.</param>
    /// <param name="language">Interface language.</param>
    internal DelimitedDocumentPreview(FileBuffer file, InterfaceLanguage language)
    {
        InitializeComponent();
        _file = file;
        AutomationProperties.SetName(Cells, file.Original.Path);
        Notice.Text = MessageCatalog.Get("common.loading", language);
        Notice.IsVisible = true;
    }

    /// <summary>Gets the table template's scroller.</summary>
    private ScrollViewer? Scroll => Cells.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    /// <summary>Retains the scroll position when another workbench tab is selected.</summary>
    internal void Capture()
    {
        if (Scroll is { } scroll && Cells.ItemCount > 0)
        {
            _file.TableOffset = scroll.Offset;
        }
    }

    /// <summary>Displays parsed data after its background read completes.</summary>
    /// <param name="rows">Rows in file order.</param>
    internal void Show(IReadOnlyList<string[]> rows)
    {
        Notice.IsVisible = false;
        Cells.Columns.Add(Column(48, row => row.Number.ToString(CultureInfo.InvariantCulture)));
        var width = rows.Select(row => row.Length).DefaultIfEmpty().Max();
        for (var index = 0; index < width; index++)
        {
            var column = index;
            Cells.Columns.Add(Column(160, row => column < row.Values.Length ? row.Values[column] : string.Empty));
        }

        Cells.ItemsSource = rows.Select((row, index) => new Row(index + 1, row)).ToArray();
        Dispatcher.UIThread.Post(
            () =>
            {
                if (Scroll is { } scroll)
                {
                    scroll.Offset = _file.TableOffset;
                }
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>Reports a malformed or oversized table.</summary>
    /// <param name="language">Interface language.</param>
    internal void ShowFailure(InterfaceLanguage language)
    {
        Notice.Text = MessageCatalog.Get("chat.the_table_is_invalid_or", language);
        Notice.IsVisible = true;
    }

    /// <summary>Builds a recycled cell template for a single column.</summary>
    /// <param name="width">Column width.</param><param name="value">Cell reader.</param>
    /// <returns>The table column.</returns>
    private static TableViewColumn Column(double width, System.Func<Row, string> value) =>
        new()
        {
            Width = new GridLength(width),
            CellTemplate = new FuncDataTemplate<Row>((row, _) =>
            {
                var cell = new TextBlock
                {
                    Text = row is null ? string.Empty : value(row),
                    Height = 30,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Padding = new Thickness(6, 6, 6, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                ToolTip.SetTip(cell, cell.Text);
                return cell;
            }),
        };

    /// <summary>A table row and its visible one-based file index.</summary>
    /// <param name="Number">One-based row number.</param><param name="Values">Cells in file order.</param>
    private sealed record Row(int Number, string[] Values);
}
