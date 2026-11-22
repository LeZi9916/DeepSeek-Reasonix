using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Shell.Composer;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
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
        w.FindControl<Button>("ModelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        picker.FindControl<TextBox>("Search")!.Text = "kimi";
        Dispatcher.UIThread.RunJobs();
        row = picker.FindControl<StackPanel>("ModelList")!.Children.OfType<Button>().Single();
        row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("/model", calls);
        Assert.Equal("kimi-k2-vision", w.FindControl<TextBlock>("ModelLabel")!.Text);
    });
}
