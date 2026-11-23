using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Shell;
using Reasonix.Studio.Shell.Composer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>A provider chip choice is retained for the next launch.</summary>
    /// <returns>The filter persistence.</returns>
    [Fact]
    public Task ModelPickerPersistsProviderFilter() => WithSettings(
        "conversation",
        (w, _) =>
        {
            w.FindControl<Button>("ModelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(SpinWait.SpinUntil(
                () => w.FindControl<ModelPicker>("ModelPicker")!.IsOpen,
                TimeSpan.FromSeconds(2)));
            var chips = w.FindControl<ModelPicker>("ModelPicker")!.FindControl<WrapPanel>("Chips")!;
            var chip = chips.Children.OfType<Button>().First();
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var preferences = typeof(MainWindow).GetField("_workspacePreferences", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
            var filter = Assert.IsAssignableFrom<IReadOnlyList<string>>(preferences.GetType().GetProperty("ModelFilter")!.GetValue(preferences));
            Assert.NotEmpty(filter);
        });

    /// <summary>Model settings exposes an up/down control for every provider row.</summary>
    /// <returns>The provider order controls.</returns>
    [Fact]
    public Task ModelSettingsShowsProviderOrderControls() => WithSettings(
        "settings-model",
        (w, _) =>
        {
            var buttons = w.GetVisualDescendants().OfType<Button>().ToArray();
            var ups = buttons.Where(button => Equals(button.Content, "↑")).ToArray();
            var downs = buttons.Where(button => Equals(button.Content, "↓")).ToArray();
            Assert.NotEmpty(ups);
            Assert.NotEmpty(downs);
        });

    /// <summary>Provider health is read and shown when the installation enables it.</summary>
    /// <returns>The health interaction.</returns>
    [Fact]
    public Task ModelPickerShowsProviderHealthWhenEnabled() => WithSettings(
        "conversation",
        (w, reads) =>
        {
            reads.Exchange = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath switch
            {
                "/providers/health/settings" => JsonReply(new JsonObject { ["enabled"] = true }),
                "/providers/health" => JsonReply(new JsonObject { ["connection"] = "reachable", ["wallet"] = "ok", ["checkedAt"] = 0 }),
                _ => null,
            });
            w.FindControl<Button>("ModelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(SpinWait.SpinUntil(
                () =>
                {
                    Dispatcher.UIThread.RunJobs();
                    return w.FindControl<ModelPicker>("ModelPicker")!.IsOpen;
                },
                TimeSpan.FromSeconds(2)));
            var picker = w.FindControl<ModelPicker>("ModelPicker")!;
            Assert.Contains(
                "连接正常",
                picker.FindControl<StackPanel>("ModelList")!.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
        });

    /// <summary>The model popover searches, commits a choice and keeps its effort ladder.</summary>
    /// <returns>The interaction.</returns>
    [Fact]
    public Task ModelPickerSearchesAndCommitsChoices() => WithSettings("conversation", (w, reads) =>
    {
        var status = reads.Read<JsonObject>("/status");
        var calls = new List<string>();
        reads.Exchange = async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            calls.Add(path);
            if (path == "/model")
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                status["modelRef"] = (string?)body["ref"];
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (path == "/effort")
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                status["effort"] = (string?)body["effort"];
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (path == "/status")
            {
                return JsonReply(status);
            }

            return null;
        };
        w.AdoptComposer(reads.Read<SessionStatus>("/status"));
        var picker = w.FindControl<ModelPicker>("ModelPicker")!;
        w.FindControl<Button>("ModelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(picker.IsOpen);
        var search = picker.FindControl<TextBox>("Search")!;
        Assert.False(string.IsNullOrEmpty(search.PlaceholderText));
        var chips = picker.FindControl<WrapPanel>("Chips")!;
        Assert.True(chips.Children.Count >= 2);
        search.Text = "kimi";
        Dispatcher.UIThread.RunJobs();
        var rows = picker.FindControl<StackPanel>("ModelList")!.Children.OfType<Button>().ToList();
        var row = Assert.Single(rows);
        Assert.Equal("kimi/kimi-k2-vision", (string?)row.Tag);
        var efforts = picker.FindControl<StackPanel>("EffortList")!.Children.OfType<Button>().ToList();
        Assert.NotEmpty(efforts);
        Assert.False(string.IsNullOrEmpty(picker.FindControl<TextBlock>("EffortNote")!.Text));
        var effort = efforts.Single(button => (string?)button.Tag == "high");
        effort.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("/effort", calls);
        Assert.Equal("深入", w.FindControl<TextBlock>("EffortLabel")!.Text);
        Assert.True(picker.IsOpen);
        picker.FindControl<TextBox>("Search")!.Text = "kimi";
        Dispatcher.UIThread.RunJobs();
        row = picker.FindControl<StackPanel>("ModelList")!.Children.OfType<Button>().Single();
        row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("/model", calls);
        Assert.True(picker.IsOpen);
        Assert.Equal("kimi-k2-vision", w.FindControl<TextBlock>("ModelLabel")!.Text);
    });
}
