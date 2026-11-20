using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Reconciles the generated message tables with the reference frontend's own.
/// </summary>
/// <remarks>
/// The manifest is written by <c>tools/dump-messages.mjs</c> from the tables Node
/// actually evaluated, and holds a count, a per-area count and one digest over
/// every key and both phrases. Comparing against that is what notices an edit on
/// either side: a phrase changed in one place only, a key added there and never
/// rendered, or a key rendered and later deleted there.
/// </remarks>
public sealed class MessageReconciliationTests
{
    /// <summary>
    /// Matches a placeholder, which both sides write as <c>{name}</c>.
    /// </summary>
    private static readonly Regex s_placeholder =
        new Regex("\\{(?<name>\\w+)\\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Where the report goes.
    /// </summary>
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageReconciliationTests"/> class.
    /// </summary>
    /// <param name="output">Where the report goes.</param>
    public MessageReconciliationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Checks that the shell holds the same number of messages as the reference
    /// frontend, and that each area holds the same number.
    /// </summary>
    [Fact]
    public void TheCountsAgreeWithTheReference()
    {
        var manifest = Manifest();

        _output.WriteLine($"reference: {manifest.Count} messages in {manifest.Areas.Count} areas");
        _output.WriteLine($"shell:     {MessageTables.All.Count} messages");

        Assert.Equal(manifest.Count, MessageTables.All.Count);

        var byArea = MessageTables.All.Keys
            .GroupBy(key => key.Split('.')[0], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        foreach (var area in manifest.Areas)
        {
            Assert.True(
                byArea.TryGetValue(area.Key, out var count),
                $"the shell has no messages for the {area.Key} area");
            Assert.True(
                count == area.Value,
                $"the {area.Key} area holds {count} messages; the reference holds {area.Value}");
        }

        Assert.Equal(manifest.Areas.Count, byArea.Count);
    }

    /// <summary>
    /// Checks that the text of every message matches the reference's, which the
    /// manifest records as one digest over every key and both phrases.
    /// </summary>
    /// <remarks>
    /// The manifest does not carry the phrases themselves: the shell already
    /// carries them in generated files, and a second copy in the baseline is a
    /// copy that can drift. The digest is what a single number can say about all
    /// of them, and the generator's own <c>-check</c> is what can name the file
    /// that changed.
    /// </remarks>
    [Fact]
    public void TheTextMatchesTheReference()
    {
        var manifest = Manifest();
        var actual = DigestOfShell();

        _output.WriteLine($"reference digest: {manifest.Digest}");
        _output.WriteLine($"shell digest:     {actual}");

        Assert.Equal(manifest.Digest, actual);
    }

    /// <summary>
    /// Checks that both languages of every message name the same placeholders.
    /// </summary>
    /// <remarks>
    /// A phrase that says <c>{n} 个文件</c> in Chinese and "files" in English
    /// renders the count in one language and drops it in the other, and the
    /// dropped one reads as a complete sentence about nothing.
    /// </remarks>
    [Fact]
    public void BothLanguagesNameTheSamePlaceholders()
    {
        var mismatched = new List<string>();

        foreach (var (key, message) in MessageTables.All)
        {
            var zh = Placeholders(message.Zh);
            var en = Placeholders(message.En);
            if (!zh.SetEquals(en))
            {
                mismatched.Add($"{key}: zh={{{string.Join(",", zh.Order(StringComparer.Ordinal))}}} en={{{string.Join(",", en.Order(StringComparer.Ordinal))}}}");
            }
        }

        _output.WriteLine($"messages: {MessageTables.All.Count}, mismatched: {mismatched.Count}");
        foreach (var line in mismatched.Take(20))
        {
            _output.WriteLine($"  {line}");
        }

        Assert.True(mismatched.Count == 0, $"{mismatched.Count} message(s) name different placeholders; first is {mismatched.FirstOrDefault()}");
    }

    /// <summary>
    /// Checks that no message is empty on either side, which would render as a
    /// blank where a sentence belongs.
    /// </summary>
    [Fact]
    public void NoMessageIsEmptyOnEitherSide()
    {
        var empty = MessageTables.All
            .Where(entry => string.IsNullOrWhiteSpace(entry.Value.Zh) || string.IsNullOrWhiteSpace(entry.Value.En))
            .Select(entry => entry.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.True(empty.Count == 0, $"{empty.Count} message(s) are empty on one side: {string.Join(", ", empty.Take(10))}");
    }

    /// <summary>
    /// Checks that every key is shaped the way the interface names one, so a
    /// stray row cannot slip into a table and never be found.
    /// </summary>
    [Fact]
    public void EveryKeyIsAreaDotMeaning()
    {
        var malformed = MessageTables.All.Keys
            .Where(key => !Regex.IsMatch(key, "^[a-z][a-z0-9_]*(\\.[a-z0-9_]+)+$", RegexOptions.CultureInvariant))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.True(malformed.Count == 0, $"malformed keys: {string.Join(", ", malformed.Take(10))}");
    }

    /// <summary>
    /// Reads the reference manifest.
    /// </summary>
    /// <returns>The parsed manifest.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the file is missing or does not parse.</exception>
    private static MessageManifest Manifest()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "baseline", "messages.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                "baseline/messages.json is missing; run node desktop/avalonia/tools/dump-messages.mjs");
        }

        return JsonSerializer.Deserialize<MessageManifest>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("baseline/messages.json did not parse");
    }

    /// <summary>
    /// Reads the placeholders a phrase names.
    /// </summary>
    /// <param name="text">The phrase.</param>
    /// <returns>The distinct names it holds.</returns>
    private static HashSet<string> Placeholders(string text)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in s_placeholder.Matches(text ?? string.Empty))
        {
            names.Add(match.Groups["name"].Value);
        }

        return names;
    }

    /// <summary>
    /// Computes the same digest the reference manifest records.
    /// </summary>
    /// <returns>The digest, as a hexadecimal string the manifest can be compared with.</returns>
    /// <remarks>
    /// FNV-1a over UTF-16 code units, fed in one fixed order: key, the Chinese
    /// phrase, the English phrase, each separated by a marker no message holds.
    /// The order is what makes the digest about this table rather than about
    /// whichever dictionary enumerated first.
    /// </remarks>
    private static string DigestOfShell()
    {
        var keys = MessageTables.All.Keys.OrderBy(key => key, StringComparer.Ordinal);
        var hash = 0x811c9dc5u;

        foreach (var key in keys)
        {
            var message = MessageTables.All[key];
            foreach (var text in new[] { key, "\u0000", message.Zh, "\u0000", message.En, "\u0001" })
            {
                foreach (var c in text)
                {
                    hash ^= c;
                    hash *= 0x01000193u;
                }
            }
        }

        return "0x" + hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The shape of the reference manifest.
    /// </summary>
    private sealed class MessageManifest
    {
        /// <summary>
        /// Gets where the messages were read from.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("capturedFrom")]
        public string CapturedFrom
        {
            get; init;
        } = string.Empty;

        /// <summary>
        /// Gets how many tables were read.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("files")]
        public int Files
        {
            get; init;
        }

        /// <summary>
        /// Gets how many messages the reference holds.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("count")]
        public int Count
        {
            get; init;
        }

        /// <summary>
        /// Gets the digest over every key and both phrases.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("digest")]
        public string Digest
        {
            get; init;
        } = string.Empty;

        /// <summary>
        /// Gets how many messages each area holds.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("areas")]
        public Dictionary<string, int> Areas
        {
            get; init;
        } = new Dictionary<string, int>(StringComparer.Ordinal);
    }
}
