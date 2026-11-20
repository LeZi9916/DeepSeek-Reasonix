using Avalonia.Headless;
using Avalonia.Threading;
using Reasonix.Studio.Baseline;
using Reasonix.Studio.Shell;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Tool = Reasonix.Studio.Contracts.Wire.Tool;

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
    /// <returns>One observation per identified control and token.</returns>
    public static async Task<IReadOnlyDictionary<string, ObservedNode>> Render()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(App));

        return await session.Dispatch(
            () =>
            {
                var window = new MainWindow(connectOnOpen: false);
                window.Show();
                window.ShowState(Conversation());
                Dispatcher.UIThread.RunJobs();

                var found = new GeometryExporter(GeometryComparerOptions.Studio).Export(window);
                window.Close();
                return found;
            },
            default);
    }

    /// <summary>
    /// Builds the conversation the baseline was captured on.
    /// </summary>
    /// <returns>The state the captured window was showing.</returns>
    /// <remarks>
    /// The words are the captured ones. The gate is about boxes and inks, and a
    /// different sentence would wrap to a different height for a reason that has
    /// nothing to do with the layout being measured.
    /// </remarks>
    public static SessionState Conversation()
    {
        var items = new List<TranscriptItem>();
        for (var turn = 0; turn < 2; turn++)
        {
            items.Add(new UserItem
            {
                Id = $"user-{turn}",
                Text = $"第 {turn} 个问题，麻烦看一下这个文件。",
            });
            items.Add(new SayItem
            {
                Id = $"say-{turn}",
                Text = $"第 {turn} 段回答，说明刚才那一步做了什么。\n\n- 要点一\n- 要点二\n",
                Reasoning = "先看文件再决定改哪里。",
                Done = true,
            });
            items.Add(new ToolItem
            {
                Id = $"tool-{turn}",
                Call = new Tool
                {
                    Name = "edit_file",
                    Args = $"{{\"path\":\"internal/pkg/mod{turn}/file{turn}.go\"}}",
                    ReadOnly = false,
                },
            });
        }

        return new SessionState { Items = items };
    }

    /// <summary>
    /// Reads the captured conversation.
    /// </summary>
    /// <returns>The parsed domain.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the file does not parse.</exception>
    public static BaselineDomain Domain()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "baseline", DomainFile);
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
}
