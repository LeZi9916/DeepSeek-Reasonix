using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.Rail;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Double-click edits the local row and commits changed text only once.</summary>
    /// <returns>The native mouse and editor keyboard flow.</returns>
    [Fact]
    public Task SidebarDoubleClickRenamesInlineAndEscapeCancels() => WithSettings("conversation", (w, _) =>
    {
        var rail = w.FindControl<ProjectRail>("Projects")!;
        var tree = new[] { new TreeWorkspace { Root = "/edit", Name = "edit", Sessions = new[] { new TreeSession { Path = "/edit/a", Title = "Alpha" } } } };
        rail.Show(tree, null);
        rail.QuickActions = true;
        var actions = new List<WorkspaceAction>();
        rail.ActionRequested += actions.Add;
        rail.NavigateRequested += _ => { };
        Dispatcher.UIThread.RunJobs();
        var row = rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:session.open");
        void DoubleClick(bool refresh = false)
        {
            var point = row.TranslatePoint(new Point(70, 15), w)!.Value;
            w.MouseDown(point, MouseButton.Left);
            w.MouseUp(point, MouseButton.Left);
            if (refresh)
            {
                rail.Show(tree, null);
                Dispatcher.UIThread.RunJobs();
                row = rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:session.open");
                point = row.TranslatePoint(new Point(70, 15), w)!.Value;
            }

            w.MouseDown(point, MouseButton.Left);
            w.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        DoubleClick(true);
        var editor = Assert.Single(rail.GetVisualDescendants().OfType<TextBox>(), box => box.IsEffectivelyVisible && AutomationProperties.GetAutomationId(box) == "action:session.rename");
        Assert.Equal("Alpha", editor.Text);
        Assert.True(editor.Bounds.Right <= Assert.IsType<Grid>(editor.Parent).FindControl<Button>("QuickPin")!.Bounds.Left);
        editor.Text = "  Beta  ";
        rail.Show(tree, "/edit/a");
        Dispatcher.UIThread.RunJobs();
        editor = Assert.Single(rail.GetVisualDescendants().OfType<TextBox>(), box => box.IsEffectivelyVisible && AutomationProperties.GetAutomationId(box) == "action:session.rename");
        Assert.Equal("  Beta  ", editor.Text);
        Assert.Empty(actions);
        w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        w.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Dispatcher.UIThread.RunJobs();
        var rename = Assert.Single(actions, action => action.Action == "session.rename");
        Assert.Equal("Beta", rename.Value);
        Assert.Equal(new SessionOpenTarget(string.Empty, "/edit", "/edit/a"), rename.Target);
        Assert.False(editor.IsVisible);
        row = rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:session.open");
        DoubleClick();
        Assert.True(editor.IsVisible);
        editor.Text = "Cancelled";
        w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        w.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(actions, action => action.Action == "session.rename");
        Assert.False(editor.IsVisible);
        DoubleClick();
        Assert.True(editor.IsVisible);
        editor.Text = "   ";
        w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        w.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Dispatcher.UIThread.RunJobs();
        Assert.Single(actions, action => action.Action == "session.rename");
        Assert.False(editor.IsVisible);
        var single = row.TranslatePoint(new Point(70, 15), w)!.Value;
        w.MouseDown(single, MouseButton.Left);
        w.MouseUp(single, MouseButton.Left);
        var composer = w.FindControl<TextBox>("Composer")!;
        var outside = composer.TranslatePoint(new Point(20, 10), w)!.Value;
        w.MouseDown(outside, MouseButton.Left);
        w.MouseUp(outside, MouseButton.Left);
        w.MouseDown(single, MouseButton.Left);
        w.MouseUp(single, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.False(editor.IsVisible);
        rail.ShowRun(string.Empty, "/edit/a", "running", true);
        Dispatcher.UIThread.RunJobs();
        var running = rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:session.open");
        Assert.True(Assert.IsType<MenuFlyout>(running.ContextFlyout).Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:session.delete").IsEnabled);
    });

    /// <summary>Session movement uses canonical archive peers and respects search and row focus.</summary>
    /// <returns>The native row keyboard flow.</returns>
    [Fact]
    public Task SidebarSessionKeysRespectMoveBoundsAndSelection() => WithSettings("conversation", (w, _) =>
    {
        var rail = w.FindControl<ProjectRail>("Projects")!;
        var sessions = new[] { new TreeSession { Path = "/move/a", Title = "A", Pinned = true }, new TreeSession { Path = "/move/b", Title = "B" }, new TreeSession { Path = "/move/c", Title = "C", Archived = true } };
        var tree = new[] { new TreeWorkspace { Root = "/move", Name = "move", Sessions = sessions } };
        rail.Show(tree, null);
        var actions = new List<WorkspaceAction>();
        rail.ActionRequested += actions.Add;
        Dispatcher.UIThread.RunJobs();
        Button Row(string label) => rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:session.open" && AutomationProperties.GetName(button) == label);
        MenuItem Item(Button row, string id) => Assert.IsType<MenuFlyout>(row.ContextFlyout).Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:" + id);
        Assert.False(Item(Row("A"), "session.move-up").IsEnabled);
        Assert.True(Item(Row("A"), "session.move-down").IsEnabled);
        Assert.False(Item(Row("B"), "session.move-down").IsEnabled);
        Row("A").Focus();
        w.KeyPress(Key.Up, RawInputModifiers.Alt, PhysicalKey.ArrowUp, string.Empty);
        w.KeyRelease(Key.Up, RawInputModifiers.Alt, PhysicalKey.ArrowUp, string.Empty);
        Assert.Empty(actions);
        w.KeyPress(Key.Down, RawInputModifiers.Alt, PhysicalKey.ArrowDown, string.Empty);
        w.KeyRelease(Key.Down, RawInputModifiers.Alt, PhysicalKey.ArrowDown, string.Empty);
        Assert.Equal("session.move-down", Assert.Single(actions).Action);
        w.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, string.Empty);
        w.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, string.Empty);
        Assert.Equal("session.delete", actions.Last().Action);
        rail.WindowFor("needle", false);
        rail.Show(tree, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(Item(Row("A"), "session.move-down").IsEnabled);
        var before = actions.Count;
        var project = rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:workspace.fold");
        Assert.IsType<MenuFlyout>(project.ContextFlyout).Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:session.selection").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Row("A").Focus();
        w.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, string.Empty);
        w.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, string.Empty);
        Assert.Equal(before, actions.Count);
    });

    /// <summary>Project drops place a remembered root before the acknowledged next root without optimistic sorting.</summary>
    /// <returns>The native drag target flow.</returns>
    [Fact]
    public Task SidebarProjectDropUsesCanonicalPlacementAndSearchGuard() => WithSettings("conversation", (w, _) =>
    {
        var rail = w.FindControl<ProjectRail>("Projects")!;
        var tree = new[] { new TreeWorkspace { Root = "/a", Name = "a", Remembered = true }, new TreeWorkspace { Root = "/b", Name = "b", Remembered = true }, new TreeWorkspace { Root = "/c", Name = "c", Remembered = true }, new TreeWorkspace { Root = "/launch", Name = "launch" } };
        rail.Show(tree, null);
        var actions = new List<WorkspaceAction>();
        rail.ActionRequested += actions.Add;
        Dispatcher.UIThread.RunJobs();
        Button Row(string label) => rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:workspace.fold" && AutomationProperties.GetName(button) == label);
        using var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DataFormat.CreateStringApplicationFormat("reasonix-workspace"), "/a"));
        void Drop(string name, double y)
        {
            var at = Row(name).TranslatePoint(new Point(80, y), w)!.Value;
            w.DragDrop(at, RawDragEventType.DragEnter, data, DragDropEffects.Move);
            w.DragDrop(at, RawDragEventType.DragOver, data, DragDropEffects.Move);
            w.DragDrop(at, RawDragEventType.Drop, data, DragDropEffects.Move);
            Dispatcher.UIThread.RunJobs();
        }

        Drop("b", 25);
        var place = Assert.Single(actions);
        Assert.Equal("workspace.place", place.Action);
        Assert.Equal("/a", place.Target.WorkspaceRoot);
        Assert.Equal("/c", place.Value);
        Assert.Equal(new[] { "a", "b", "c", "launch" }, rail.GetVisualDescendants().OfType<Button>().Where(button => AutomationProperties.GetAutomationId(button) == "action:workspace.fold").Select(AutomationProperties.GetName));
        Drop("b", 2);
        Drop("a", 2);
        Drop("launch", 25);
        Assert.Single(actions);
        rail.WindowFor("needle", false);
        rail.Show(tree, null);
        Dispatcher.UIThread.RunJobs();
        Drop("c", 25);
        Assert.Single(actions);
    });

    /// <summary>Native folder drops reject multiple items and dispatch one canonical local project add.</summary>
    /// <returns>The native storage drop flow.</returns>
    [Fact]
    public Task SidebarFolderDropAcceptsExactlyOneLocalFolder() => WithSettings("conversation", (w, _) =>
    {
        var rail = w.FindControl<ProjectRail>("Projects")!;
        var actions = new List<WorkspaceAction>();
        rail.ActionRequested += actions.Add;
        Dispatcher.UIThread.RunJobs();
        var at = rail.TranslatePoint(new Point(60, 80), w)!.Value;
        var folderPath = Directory.CreateTempSubdirectory("reasonix-sidebar-drop-").FullName;
        using var folder = w.StorageProvider.TryGetFolderFromPathAsync(folderPath).GetAwaiter().GetResult()!;
        Assert.NotNull(folder);
        using var data = new DataTransfer();
        data.Add(DataTransferItem.CreateFile(folder));
        w.DragDrop(at, RawDragEventType.DragEnter, data, DragDropEffects.Copy);
        w.DragDrop(at, RawDragEventType.Drop, data, DragDropEffects.Copy);
        Dispatcher.UIThread.RunJobs();
        var add = Assert.Single(actions);
        Assert.Equal("workspace.add-path", add.Action);
        Assert.Equal(folderPath, add.Target.WorkspaceRoot);
        data.Add(DataTransferItem.CreateFile(folder));
        w.DragDrop(at, RawDragEventType.DragEnter, data, DragDropEffects.Copy);
        w.DragDrop(at, RawDragEventType.Drop, data, DragDropEffects.Copy);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(actions, action => action.Action == "workspace.add-path");
        Directory.Delete(folderPath);
    });
}
