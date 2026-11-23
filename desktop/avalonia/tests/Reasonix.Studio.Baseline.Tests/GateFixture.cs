using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Baseline;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Events;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell;
using Reasonix.Studio.Shell.Host;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// What the geometry cases share: the window rendered on the captured
/// conversation, the baseline it is compared with, and the differences already
/// recorded.
/// </summary>
/// <remarks>
/// It lives in one place so the gate and the coverage case cannot drift apart in
/// what they measure. Two copies of a fixture is two claims about which page the
/// baseline describes, and the one that goes stale is the one nobody re-reads.
/// </remarks>
internal static class GateFixture
{
    /// <summary>
    /// The domain every case compares against.
    /// </summary>
    public const string DomainFile = "conversation.light.json";

    /// <summary>
    /// Renders the window on the captured conversation and exports its tree.
    /// </summary>
    /// <param name="dark">Whether to render the dark reference scheme.</param>
    /// <returns>One observation per identified control and token.</returns>
    public static async Task<IReadOnlyDictionary<string, ObservedNode>> Render(bool dark = false)
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(App));

        return await session.Dispatch(
            () =>
            {
                using var handler = new ComposerReads();
                using var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:4000") };
                var engine = new SessionEngine(new KernelClient(http), new SseEventTransport(http.BaseAddress.ToString(), http));

                // The captured composer has the optional mode picker switched on.
                var preferences = new WorkspacePreferences { ShowModePicker = true };
                var window = new MainWindow(false, engine, InterfaceLanguage.Zh, workspacePreferences: preferences)
                {
                    RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
                };
                window.Show();
                window.AdoptComposer(handler.Status);
                window.ShowState(Reasonix.Studio.Testing.ReferenceConversation.Create());
                Dispatcher.UIThread.RunJobs();

                // The DOM capture measures descendants of closed details; expose native
                // work bodies so the coverage audit still measures every recorded row.
                foreach (var toggle in window.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("activity-toggle")).ToArray())
                {
                    toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }

                Dispatcher.UIThread.RunJobs();
                var found = new GeometryExporter(GeometryComparerOptions.Studio).Export(window);
                window.Close();
                return found;
            },
            default);
    }

    /// <summary>
    /// Reads the captured conversation.
    /// </summary>
    /// <param name="dark">Whether to read the dark reference scheme.</param>
    /// <returns>The parsed domain.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the file does not parse.</exception>
    public static BaselineDomain Domain(bool dark = false)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "baseline", dark ? "conversation.dark.json" : DomainFile);
        return JsonSerializer.Deserialize<BaselineDomain>(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"{DomainFile} did not parse into a domain");
    }

    /// <summary>
    /// Names one finding the way the debt file spells it.
    /// </summary>
    /// <param name="finding">The finding to name.</param>
    /// <returns>Its kind and identity, which is what the file records.</returns>
    public static string KeyOf(GeometryFinding finding)
    {
        return finding.Kind + " " + finding.Identity;
    }

    /// <summary>
    /// Reads the differences the gate already knows about.
    /// </summary>
    /// <returns>One entry per recorded difference.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the file is missing or does not parse.</exception>
    public static HashSet<string> ReadDebt()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "baseline", "gate-debt.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                "baseline/gate-debt.json is missing; the gate has nothing to compare its findings with");
        }

        var recorded = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("baseline/gate-debt.json did not parse");
        return new HashSet<string>(recorded, StringComparer.Ordinal);
    }

    /// <summary>
    /// Rewrites the debt file with the differences a run found.
    /// </summary>
    /// <param name="findings">The differences to record.</param>
    /// <returns>The path that was written.</returns>
    /// <remarks>
    /// The entries are sorted so a recording is one stable diff rather than a
    /// reordering of the same set. Only the copy beside the test binary is
    /// written: a test process runs from its output directory, and reaching back
    /// into the source tree is how a test starts editing the repository it is
    /// measuring. The caller moves the file into the baseline.
    /// </remarks>
    public static string WriteDebt(IReadOnlyList<GeometryFinding> findings)
    {
        var entries = findings
            .Select(KeyOf)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToList();
        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }) + "\n";

        var path = Path.Combine(AppContext.BaseDirectory, "baseline", "gate-debt.json");
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>
    /// Reads the baseline keys the shell has already claimed.
    /// </summary>
    /// <returns>One key per control or token the shell is expected to declare.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the file is missing or does not parse.</exception>
    /// <remarks>
    /// A key leaving this list is the failure the coverage case exists for: the
    /// comparer would only say the baseline node went missing, which reads as a
    /// layout fault, while the cause is a control that stopped declaring the
    /// identity it had. The file can only grow, except when a recording records
    /// the smaller set a change earned.
    /// </remarks>
    public static HashSet<string> ReadCovered()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "baseline", "gate-covered.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                "baseline/gate-covered.json is missing; the coverage case has nothing to hold the shell to");
        }

        var recorded = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("baseline/gate-covered.json did not parse");
        return new HashSet<string>(recorded, StringComparer.Ordinal);
    }

    /// <summary>
    /// Rewrites the list of claimed keys.
    /// </summary>
    /// <param name="observed">What this run claimed.</param>
    /// <returns>The path that was written.</returns>
    /// <remarks>
    /// Tokens are left out on purpose: they are read from the resource dictionary
    /// as one block, so they enter and leave together and a per-token list would
    /// be the same fact written 149 times. The controls are what a page can stop
    /// declaring one at a time.
    /// </remarks>
    public static string WriteCovered(IReadOnlyDictionary<string, ObservedNode> observed)
    {
        var entries = observed.Keys
            .Where(key => !key.StartsWith("token:", StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }) + "\n";

        var path = Path.Combine(AppContext.BaseDirectory, "baseline", "gate-covered.json");
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>Supplies the same captured task and repository reads as the screenshot fixture.</summary>
    private sealed class ComposerReads : HttpMessageHandler
    {
        /// <summary>The reference renderer's endpoint answers.</summary>
        private readonly JsonObject _reads = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "page-inputs.json")))!.AsObject();

        /// <summary>Gets the captured task status.</summary>
        public SessionStatus Status => _reads["/status"].Deserialize<SessionStatus>(JsonSerializerOptions.Web)!;

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/trajectory")
            {
                // The captured conversation has a transcript but no recorded analysis rows.
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"events":[]}""", System.Text.Encoding.UTF8, "application/json"),
                });
            }

            var key = _reads.ContainsKey(request.RequestUri.PathAndQuery) ? request.RequestUri.PathAndQuery : path;
            return Task.FromResult(new HttpResponseMessage(_reads.ContainsKey(key) ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new StringContent(_reads[key]?.ToJsonString() ?? "null", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
