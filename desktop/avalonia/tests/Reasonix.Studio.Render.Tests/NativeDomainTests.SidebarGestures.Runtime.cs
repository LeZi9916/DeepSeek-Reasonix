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
using Reasonix.Studio.Kernel.Boundary;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Native sidebar gestures persist through the actual Windows kernel and its canonical tree.</summary>
    /// <returns>The isolated real-host mouse, keyboard and file-drop flow.</returns>
    [Fact]
    public async Task SidebarGesturesPersistThroughRealWindowsHost()
    {
        var binary = HostLauncher.Locate();
        if (binary is null || !OperatingSystem.IsWindows())
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("STUDIO_TEST_HOST_REQUIRED"));
            return;
        }

        var scratch = Directory.CreateTempSubdirectory("studio-sidebar-gestures-");
        Process? hosted = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var token = timeout.Token;
            var first = scratch.CreateSubdirectory("a").FullName;
            var second = scratch.CreateSubdirectory("b").FullName;
            var third = scratch.CreateSubdirectory("c").FullName;
            var dropped = scratch.CreateSubdirectory("dropped").FullName;
            using var kernel = await HostLauncher.StartAsync(binary, Array.Empty<string>(), token, scratch.CreateSubdirectory("profile").FullName);
            hosted = Process.GetProcessById(kernel.Pid);
            using var http = new HttpClient(new CredentialHandler(kernel.Cookie())) { BaseAddress = kernel.Origin };
            await using var owner = new SessionEngine(kernel);
            foreach (var root in new[] { first, second, third })
            {
                using var registered = await http.PostAsJsonAsync("/tree/workspaces", new JsonObject { ["path"] = root }, token);
                Assert.True(registered.IsSuccessStatusCode, await registered.Content.ReadAsStringAsync(token));
            }

            var firstPath = await Import("First");
            var secondPath = await Import("Second");
            var initial = await owner.TreeAsync(null, token);
            var current = initial.Single(project => project.Root == first).Sessions[0];
            var other = current.Path == firstPath ? secondPath : firstPath;
            await using var active = await owner.OpenProjectAsync(first, token, false, current.Path);
            var session = HeadlessUnitTestSession.StartNew(typeof(RasterBuilder));
            await session.Dispatch(
                async () =>
                {
                    var window = new MainWindow(false, active, InterfaceLanguage.En, _ => Task.FromResult(false)) { Width = 1440, Height = 900 };
                    window.Show();
                    try
                    {
                        window.AdoptComposer((await active.StatusAsync(token).ConfigureAwait(true))!);
                        var rail = window.FindControl<ProjectRail>("Projects")!;
                        rail.Show(initial, current.Path);
                        Dispatcher.UIThread.RunJobs();
                        var row = Row("action:session.open", current.Title);
                        var point = row.TranslatePoint(new Point(70, 15), window)!.Value;
                        window.MouseDown(point, MouseButton.Left);
                        window.MouseUp(point, MouseButton.Left);
                        await Task.Delay(650, token).ConfigureAwait(true);
                        row = Row("action:session.open", current.Title);
                        point = row.TranslatePoint(new Point(70, 15), window)!.Value;
                        window.MouseDown(point, MouseButton.Left);
                        window.MouseUp(point, MouseButton.Left);
                        window.MouseDown(point, MouseButton.Left);
                        window.MouseUp(point, MouseButton.Left);
                        Dispatcher.UIThread.RunJobs();
                        var editor = Assert.Single(rail.GetVisualDescendants().OfType<TextBox>(), box => box.IsEffectivelyVisible && AutomationProperties.GetAutomationId(box) == "action:session.rename");
                        editor.Text = "  Renamed from native row  ";
                        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                        await Wait(async () => (await owner.TreeAsync(null, token).ConfigureAwait(true)).SelectMany(project => project.Sessions).Any(item => item.Path == current.Path && item.Title == "Renamed from native row")).ConfigureAwait(true);
                        await Wait(() => Task.FromResult(rail.SearchEntries.Any(entry => entry.Session.Title == "Renamed from native row"))).ConfigureAwait(true);

                        Row("action:session.open", "Renamed from native row").Focus();
                        window.KeyPress(Key.Down, RawInputModifiers.Alt, PhysicalKey.ArrowDown, string.Empty);
                        window.KeyRelease(Key.Down, RawInputModifiers.Alt, PhysicalKey.ArrowDown, string.Empty);
                        await Wait(async () => (await owner.TreeAsync(null, token).ConfigureAwait(true)).Single(project => project.Root == first).Sessions[1].Path == current.Path).ConfigureAwait(true);

                        using var projectData = new DataTransfer();
                        var projectOrder = rail.LocalProjects.Where(project => project.Remembered).ToArray();
                        projectData.Add(DataTransferItem.Create(DataFormat.CreateStringApplicationFormat("reasonix-workspace"), projectOrder[0].Root));
                        var target = Row("action:workspace.fold", projectOrder[1].Name).TranslatePoint(new Point(70, 25), window)!.Value;
                        window.DragDrop(target, RawDragEventType.DragEnter, projectData, DragDropEffects.Move);
                        window.DragDrop(target, RawDragEventType.DragOver, projectData, DragDropEffects.Move);
                        window.DragDrop(target, RawDragEventType.Drop, projectData, DragDropEffects.Move);
                        await Wait(async () => (await owner.TreeAsync(null, token).ConfigureAwait(true)).Where(project => project.Remembered).Select(project => project.Root).SequenceEqual(new[] { projectOrder[1].Root, projectOrder[0].Root, projectOrder[2].Root })).ConfigureAwait(true);

                        await Wait(() => Task.FromResult(rail.SearchEntries.Any(entry => entry.Session.Path == other))).ConfigureAwait(true);
                        var otherTitle = rail.SearchEntries.Single(entry => entry.Session.Path == other).Session.Title;
                        Assert.True(Row("action:session.open", otherTitle).Focus());
                        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, string.Empty);
                        window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, string.Empty);
                        Dispatcher.UIThread.RunJobs();
                        await Wait(() => Task.FromResult(window.GetVisualDescendants().OfType<Button>().Any(button => button.IsEffectivelyVisible && AutomationProperties.GetAutomationId(button) == "action:workspace.confirm"))).ConfigureAwait(true);
                        window.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:workspace.confirm").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await Wait(async () => !(await owner.TreeAsync(null, token).ConfigureAwait(true)).SelectMany(project => project.Sessions).Any(item => item.Path == other)).ConfigureAwait(true);
                        Assert.False(File.Exists(other));
                        Assert.True(File.Exists(current.Path));

                        using var folder = (await window.StorageProvider.TryGetFolderFromPathAsync(dropped).ConfigureAwait(true))!;
                        Assert.NotNull(folder);
                        using var fileData = new DataTransfer();
                        fileData.Add(DataTransferItem.CreateFile(folder));
                        target = rail.TranslatePoint(new Point(60, 80), window)!.Value;
                        window.DragDrop(target, RawDragEventType.DragEnter, fileData, DragDropEffects.Copy);
                        window.DragDrop(target, RawDragEventType.Drop, fileData, DragDropEffects.Copy);
                        await Wait(async () => (await owner.TreeAsync(null, token).ConfigureAwait(true)).Any(project => project.Root == dropped && project.Remembered)).ConfigureAwait(true);
                        await Wait(async () => (await owner.Pages.RuntimesAsync(token).ConfigureAwait(true)).Any(runtime => runtime.Root == dropped)).ConfigureAwait(true);
                        await Wait(() => Task.FromResult(window.FindControl<TextBlock>("ProjectName")!.Text == "dropped")).ConfigureAwait(true);
                    }
                    finally
                    {
                        window.Close();
                        Dispatcher.UIThread.RunJobs();
                    }

                    return 0;

                    Button Row(string action, string label)
                    {
                        Dispatcher.UIThread.RunJobs();
                        return window.FindControl<ProjectRail>("Projects")!.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == action && AutomationProperties.GetName(button) == label);
                    }

                    async Task Wait(Func<Task<bool>> ready)
                    {
                        var watch = Stopwatch.StartNew();
                        while (!await ready().ConfigureAwait(true) && watch.Elapsed < TimeSpan.FromSeconds(12))
                        {
                            await Task.Delay(10, token).ConfigureAwait(true);
                        }

                        Assert.True(await ready().ConfigureAwait(true), "The real host did not acknowledge the sidebar gesture. " + window.FindControl<SelectableTextBlock>("ErrorText")!.Text);
                    }
                },
                token);

            async Task<string> Import(string title)
            {
                var archive = new JsonObject
                {
                    ["format"] = "reasonix-native-session",
                    ["version"] = 2,
                    ["records"] = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = "Sidebar gesture fixture." }, new JsonObject { ["role"] = "user", ["content"] = "question" }, new JsonObject { ["role"] = "assistant", ["content"] = "answer" }),
                };
                using var imported = await http.PostAsJsonAsync("/tree/sessions/import-native?workspace=" + Uri.EscapeDataString(first), archive, token);
                Assert.True(imported.IsSuccessStatusCode, await imported.Content.ReadAsStringAsync(token));
                var path = (string)(await imported.Content.ReadFromJsonAsync<JsonObject>(token))!["path"]!;
                await owner.Pages.WorkspaceActionAsync("sessions/rename", new JsonObject { ["path"] = path, ["title"] = title }, token);
                return path;
            }
        }
        finally
        {
            if (hosted is not null)
            {
                using var stopped = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await hosted.WaitForExitAsync(stopped.Token);
                hosted.Dispose();
            }

            scratch.Delete(true);
        }
    }
}
