using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages;
using Reasonix.Studio.Shell.Host;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Fades the incoming workbench canvas once per selected surface.</summary>
    /// <param name="dark">Whether to use the dark palette.</param>
    /// <returns>The file tab selection check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task WorkbenchCanvasFadesOnSelection(bool dark) =>
        WithSettingsAsync(
            "workbench",
            async (w, _) =>
            {
                w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                var canvas = w.FindControl<Grid>("WorkspaceCanvas")!;
                var content = w.FindControl<Grid>("FileCanvas")!;
                await WaitForMotionFrame(() => !content.IsAnimating(Visual.OpacityProperty)).ConfigureAwait(true);
                ClickAt(w, "action:workbench.new");
                Assert.False(canvas.IsEffectivelyVisible);
                ClickAt(w, "action:workbench.tab");
                await WaitForMotionFrame(() => content.Opacity is > 0.53 and < 0.92).ConfigureAwait(true);
                var scheme = dark ? "dark" : "light";
                CaptureMotionRegion(w, canvas, -5, 4, 294, 164, $"workbench-canvas-{scheme}-45");
                await WaitForMotionFrame(() => !content.IsAnimating(Visual.OpacityProperty)).ConfigureAwait(true);
                CaptureMotionRegion(w, canvas, -5, 4, 294, 164, $"workbench-canvas-{scheme}-final");
                ClickAt(w, "action:workbench.tab");
                Assert.False(content.IsAnimating(Visual.OpacityProperty));
            },
            allowMotion: true);

    /// <summary>Persists a named message star and reopens it in an isolated desktop profile.</summary>
    /// <returns>The restored marker selection.</returns>
    [Fact]
    public Task MessageRailStarsSurviveProfileReload() =>
        WithSettings(
            "conversation",
            (w, _) =>
            {
                var profile = Directory.CreateTempSubdirectory("rail-bookmarks-");
                try
                {
                    var file = Path.Combine(profile.FullName, "workspace.json");
                    var page = new SessionPage { Width = 700, Height = 600 };
                    var items = Enumerable.Range(1, 3)
                        .SelectMany(index => new TranscriptItem[]
                        {
                            new UserItem { Id = $"q{index}", MsgIndex = index * 2, Text = $"Section {index}" },
                            new SayItem { Id = $"a{index}", Done = true, Text = string.Join("\n\n", Enumerable.Repeat("Recorded result.", 6)) },
                        }).ToArray();
                    var state = new SessionState { Items = items };
                    var owner = new object();
                    page.ConfigureMessageRailBookmarks(
                        WorkspacePreferences.Load(file).MessageRailBookmarks,
                        (scope, marks) =>
                        {
                            var preferences = new WorkspacePreferences
                            {
                                MessageRailBookmarks = new Dictionary<string, int[]> { [scope] = marks },
                            };
                            preferences.Save(file);
                        });
                    page.SetMessageRailScope(string.Empty, owner);
                    page.Show(state);
                    var grid = w.FindControl<Grid>("LayoutRoot")!;
                    grid.Children.Add(page);
                    page.ZIndex = 99;
                    w.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    var track = page.GetVisualDescendants().OfType<Canvas>().Single(canvas => canvas.Name == "Track");
                    var mark = track.Children.OfType<Button>().ElementAt(1);
                    var point = mark.TranslatePoint(new Point(8, 5), w)!.Value;
                    w.MouseMove(point);
                    w.MouseDown(point, MouseButton.Right);
                    w.MouseUp(point, MouseButton.Right);
                    Assert.Contains("starred", mark.Classes);
                    Assert.False(File.Exists(file));
                    page.SetMessageRailScope("named-session", owner);
                    Assert.Equal(new[] { 4 }, WorkspacePreferences.Load(file).MessageRailBookmarks["named-session"]);

                    var restored = new SessionPage { Width = 700, Height = 600 };
                    restored.ConfigureMessageRailBookmarks(WorkspacePreferences.Load(file).MessageRailBookmarks, (_, _) => { });
                    restored.SetMessageRailScope("named-session", new object());
                    restored.Show(state);
                    grid.Children.Remove(page);
                    grid.Children.Add(restored);
                    restored.ZIndex = 99;
                    w.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    var restoredTrack = restored.GetVisualDescendants().OfType<Canvas>().Single(canvas => canvas.Name == "Track");
                    Assert.Contains("starred", restoredTrack.Children.OfType<Button>().ElementAt(1).Classes);
                    restored.SetMessageRailScope("other-session", new object());
                    Assert.DoesNotContain("starred", restoredTrack.Children.OfType<Button>().ElementAt(1).Classes);
                }
                finally
                {
                    profile.Delete(recursive: true);
                }
            });
}
