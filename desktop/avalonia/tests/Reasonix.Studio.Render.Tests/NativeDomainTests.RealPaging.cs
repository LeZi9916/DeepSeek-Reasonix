using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Kernel.Boundary;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell;
using Reasonix.Studio.State.Transcript;
using System;
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
    /// <summary>Pages a real imported transcript through native pointer actions and opaque host cursors.</summary>
    /// <returns>The isolated Windows host and complete history window.</returns>
    [Fact]
    public async Task RealHistoryPagesPrependWithoutLosingTheDraftOrDuplicatingRows()
    {
        var binary = HostLauncher.Locate();
        if (binary is null || !OperatingSystem.IsWindows())
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("STUDIO_TEST_HOST_REQUIRED"));
            return;
        }

        var scratch = Directory.CreateTempSubdirectory("studio-real-paging-");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var token = timeout.Token;
            using var kernel = await HostLauncher.StartAsync(binary, [], token, scratch.CreateSubdirectory("profile").FullName);
            await using var owner = new SessionEngine(kernel);
            var project = scratch.CreateSubdirectory("project");
            await using var seed = await owner.OpenProjectAsync(project.FullName, token);
            using var http = new HttpClient(new CredentialHandler(kernel.Cookie())) { BaseAddress = kernel.Origin };
            var records = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = "Isolated paged history fixture." });
            for (var index = 0; index < 210; index++)
            {
                records.Add(new JsonObject { ["role"] = "user", ["content"] = "paged question " + index });
                records.Add(new JsonObject { ["role"] = "assistant", ["content"] = "paged answer " + index });
            }

            using var imported = await http.PostAsJsonAsync("/tree/sessions/import-native?workspace=" + Uri.EscapeDataString(project.FullName), new JsonObject { ["format"] = "reasonix-native-session", ["version"] = 2, ["records"] = records }, token);
            Assert.True(imported.IsSuccessStatusCode, await imported.Content.ReadAsStringAsync(token));
            var saved = await imported.Content.ReadFromJsonAsync<JsonObject>(token);
            var path = (string)saved!["path"]!;
            await owner.Pages.WorkspaceActionAsync("sessions/rename", new JsonObject { ["path"] = path, ["title"] = "Paged history" }, token);
            var tree = await owner.TreeAsync(null, token);
            var status = await owner.StatusAsync(token);
            var session = HeadlessUnitTestSession.StartNew(typeof(RasterBuilder));
            await session.Dispatch(
                async () =>
                {
                    var window = new MainWindow(false, owner, InterfaceLanguage.Zh, _ => Task.FromResult(false)) { Width = 1440, Height = 900 };
                    window.Show();
                    try
                    {
                        window.AdoptComposer(status!);
                        var rail = window.FindControl<ProjectRail>("Projects")!;
                        rail.Show(tree, null);
                        Dispatcher.UIThread.RunJobs();
                        var row = rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:session.open" && AutomationProperties.GetName(button) == "Paged history");
                        Click(row);
                        var conversation = window.FindControl<SessionPage>("Conversation")!;
                        await Wait(() => conversation.State.Items.OfType<SayItem>().Any(item => item.Text == "paged answer 209"));
                        Assert.DoesNotContain(conversation.State.Items, item => item is UserItem { Text: "paged question 0" });
                        var draft = window.FindControl<TextBox>("Composer")!;
                        draft.Text = "draft held while paging";
                        var more = window.FindControl<Button>("EarlierHistory")!;
                        for (var page = 0; page < 2; page++)
                        {
                            Assert.True(more.IsEffectivelyVisible);
                            var before = conversation.State.Items.Count;
                            Click(more);
                            await Wait(() => conversation.State.Items.Count > before && more.IsEnabled);
                        }

                        Assert.False(more.IsVisible);
                        Assert.Equal(420, conversation.State.Items.Count);
                        Assert.Equal(420, conversation.State.Items.Select(item => item.Id).Distinct().Count());
                        Assert.Equal("paged question 0", Assert.IsType<UserItem>(conversation.State.Items[0]).Text);
                        Assert.Equal("paged answer 209", Assert.IsType<SayItem>(conversation.State.Items[^1]).Text);
                        Assert.Equal("draft held while paging", draft.Text);
                    }
                    finally
                    {
                        window.Close();
                    }

                    void Click(Control control)
                    {
                        Dispatcher.UIThread.RunJobs();
                        var point = control.TranslatePoint(new Point(Math.Min(40, control.Bounds.Width / 2), control.Bounds.Height / 2), window);
                        Assert.NotNull(point);
                        window.MouseMove(point.Value);
                        window.MouseDown(point.Value, MouseButton.Left);
                        window.MouseUp(point.Value, MouseButton.Left);
                    }

                    async Task Wait(Func<bool> ready)
                    {
                        var deadline = DateTime.UtcNow.AddSeconds(15);
                        while (!ready() && DateTime.UtcNow < deadline)
                        {
                            Dispatcher.UIThread.RunJobs();
                            await Task.Delay(10, token).ConfigureAwait(true);
                        }

                        Assert.True(ready(), window.FindControl<SelectableTextBlock>("ErrorText")!.Text);
                    }
                },
                token);
        }
        finally
        {
            scratch.Delete(true);
        }
    }
}
