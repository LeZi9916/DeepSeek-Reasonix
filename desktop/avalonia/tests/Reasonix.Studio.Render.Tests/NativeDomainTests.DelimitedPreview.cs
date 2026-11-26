using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaloniaEdit.Document;
using Reasonix.Studio.Shell.Workbench;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Parses literal text, quoted delimiters and multiline values without executing spreadsheet expressions.</summary>
    [Fact]
    public void DelimitedPreviewKeepsQuotedFieldsAndBlankRows()
    {
        var rows = DelimitedTable.Parse("\uFEFF\"city\",note\r\nBeijing,\"one, two\r\nthree \"\"four\"\"\"\n\n=1+2,", ',');
        Assert.Equal(new[] { "city", "note" }, rows[0]);
        Assert.Equal(new[] { "Beijing", "one, two\r\nthree \"four\"" }, rows[1]);
        Assert.Equal(new[] { string.Empty }, rows[2]);
        Assert.Equal(new[] { "=1+2", string.Empty }, rows[3]);
        Assert.Equal(new[] { "left", "right" }, DelimitedTable.Parse("left\tright", '\t')[0]);
        Assert.Throws<System.FormatException>(() => DelimitedTable.Parse("\"unfinished", ','));
    }

    /// <summary>Finds nested multi-line code ranges without interpreting braces in strings or comments.</summary>
    [Fact]
    public void WorkbenchFoldingPreservesQuotedAndCommentedBraces()
    {
        var source = new TextDocument("func main() {\n  // }\n  print(\"}\")\n  if ok {\n    run()\n  }\n}");
        var folds = WorkbenchCodeFolds.Create(source, ".go");
        Assert.Equal(2, folds.Length);
        Assert.Equal(source.TextLength, folds[0].EndOffset);
        Assert.Equal(4, source.GetLineByOffset(folds[1].StartOffset).LineNumber);
    }

    /// <summary>Opens an authorized table from the workspace and preserves the source draft when toggling preview.</summary>
    /// <returns>The document preview interaction.</returns>
    [Fact]
    public Task DelimitedPreviewOpensFromTheWorkbenchAndUsesTheActiveDraft() => WithSettings("workbench", (window, reads) =>
    {
        reads.Exchange = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath switch
        {
            "/workspace/files" => JsonReply(new JsonObject { ["files"] = new JsonArray("report.tsv"), ["directories"] = new JsonArray() }),
            "/workspace/preview/info" => JsonReply(new JsonObject { ["kind"] = "table", ["version"] = "v1" }),
            "/workspace/file" => JsonReply(new JsonObject { ["path"] = "report.tsv", ["content"] = "name\tamount\nA\t12", ["revision"] = "v1" }),
            _ => null,
        });
        window.FindControl<Button>("FilesTab")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var files = window.FindControl<ListBox>("WorkspaceFiles")!;
        PumpNavigation(() => files.Items.OfType<WorkspaceFileEntry>().Any(file => file.Path == "report.tsv"));
        files.SelectedItem = files.Items.OfType<WorkspaceFileEntry>().Single(file => file.Path == "report.tsv");
        PumpNavigation(() => Table()?.ItemCount == 2);
        Assert.Equal(3, Table()!.Columns.Count);
        window.FindControl<Button>("EditFile")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var source = window.FindControl<WorkbenchFileEditor>("FileEditor")!;
        Assert.True(source.IsEffectivelyVisible);
        source.Text += "\nB\t18";
        window.FindControl<Button>("PreviewFile")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpNavigation(() => Table()?.ItemCount == 3);
        Assert.EndsWith("B\t18", source.Text, StringComparison.Ordinal);

        TableView? Table() => window.GetVisualDescendants().OfType<TableView>().SingleOrDefault(table => table.IsEffectivelyVisible);
    });

    /// <summary>Routes workbench shortcuts to the source editor without moving to the transcript.</summary>
    /// <returns>The native editor navigation.</returns>
    [Fact]
    public Task WorkbenchEditorFindAndGoToLineKeepTheDraft() => WithSettings("workbench", (window, _) =>
    {
        window.FindControl<Button>("EditFile")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var editor = window.FindControl<WorkbenchFileEditor>("FileEditor")!;
        editor.Text = "first\nsecond result\nthird";
        editor.TextArea.Focus();
        var primary = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
        window.KeyPress(Key.G, primary, PhysicalKey.G, string.Empty);
        window.KeyRelease(Key.G, primary, PhysicalKey.G, string.Empty);
        var target = window.FindControl<TextBox>("FileGotoLine")!;
        Assert.True(target.IsEffectivelyVisible);
        target.Text = "3";
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Assert.Equal(3, editor.CurrentLine);
        window.KeyPress(Key.F, primary, PhysicalKey.F, string.Empty);
        window.KeyRelease(Key.F, primary, PhysicalKey.F, string.Empty);
        Assert.True(editor.SearchPanel.IsOpened);
        editor.SearchPanel.SearchPattern = "second";
        editor.SearchPanel.FindNext(0);
        Assert.Equal("second", editor.SelectedText);
        Assert.Equal("first\nsecond result\nthird", editor.Text);
    });
}
