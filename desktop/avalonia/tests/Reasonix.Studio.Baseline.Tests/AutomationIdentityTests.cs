using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Reasonix.Studio.Baseline;
using Reasonix.Studio.Pages;
using Reasonix.Studio.Shell;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the identities the geometry gate compares by to the controls that carry
/// them, so a page cannot drift away from the baseline's names in silence.
/// </summary>
/// <remarks>
/// The gate pairs a baseline node with a control by an automation id spelled the
/// same way the capture spelled the element's identity. An id that is missing
/// does not fail loudly: the baseline's node is reported missing and the run is
/// red for a reason that looks like a layout fault. These cases fail at the
/// naming step instead.
/// </remarks>
[Collection(HeadlessCollection.Name)]
public sealed class AutomationIdentityTests
{
    /// <summary>
    /// Checks that the shell's regions carry the identities the conversation
    /// baseline recorded for them.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task TheShellRegionsCarryTheBaselinesIdentities()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(App));

        var observed = await session.Dispatch(
            () =>
            {
                var page = new MainWindow(connectOnOpen: false);
                page.Show();

                var found = new GeometryExporter(GeometryComparerOptions.Studio).Export(page);
                var keys = new List<string>(found.Keys);
                page.Close();
                return keys;
            },
            default);

        // One of each: the window draws a rail, a chrome bar, a pane and one
        // composer, and the baseline drew the same four regions once each.
        foreach (var identity in new[] { "class:rail", "class:chrome", "class:pane", "class:crumb" })
        {
            Assert.Contains(identity + "#0", observed);
        }
    }

    /// <summary>
    /// Checks that every transcript row is one <c>class:call</c> box and that the
    /// rows whose body is prose carry <c>class:txt</c>, which is the structure the
    /// reference frontend's conversation was captured in.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task TranscriptRowsCarryTheRowAndTextIdentities()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(App));

        var observed = await session.Dispatch(
            () =>
            {
                var page = new SessionPage { Width = 760, Height = 400 };
                page.Show(new SessionState
                {
                    Items = new TranscriptItem[]
                    {
                        new UserItem { Id = "u1", Text = "first" },
                        new SayItem { Id = "s1", Text = "answer", Done = true },
                        new UserItem { Id = "u2", Text = "second" },
                    },
                });

                var window = new Window { Width = 800, Height = 440, Content = page };
                window.Show();

                var found = new GeometryExporter(GeometryComparerOptions.Studio).Export(window);
                var keys = found.Keys.ToList();
                window.Close();
                return keys;
            },
            default);

        // Three rows, each one class:call, and two of them say something in
        // class:txt. The occurrence numbers follow visual-tree order, which is
        // the order the capture numbered the document's elements in.
        Assert.Contains("class:call#0", observed);
        Assert.Contains("class:call#1", observed);
        Assert.Contains("class:call#2", observed);
        Assert.Contains("class:txt#0", observed);
        Assert.Contains("class:txt#1", observed);
        Assert.DoesNotContain("class:call#3", observed);
    }
}
