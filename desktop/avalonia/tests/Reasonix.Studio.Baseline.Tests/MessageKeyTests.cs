using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the generated message keys to the tables they were derived from.
/// </summary>
/// <remarks>
/// Two things can go wrong here and only one of them fails a build:
/// <list type="bullet">
/// <item>A constant that disappears breaks every call site that names it, which
/// the compiler catches — <see cref="KnownKeysAreCompileTimeConstants"/> is that
/// guarantee made explicit, because it does not compile at all without them.</item>
/// <item>A key that is in the table and has no constant, or a constant whose text
/// no longer matches its key, compiles fine and fails only at run time. The
/// reflection cases below are what notice those.</item>
/// </list>
/// </remarks>
public sealed class MessageKeyTests
{
    /// <summary>
    /// The constants the rest of this shell may rely on.
    /// </summary>
    /// <remarks>
    /// These are written as expressions rather than strings on purpose: if the
    /// generator stops emitting a name, this file stops compiling, which is the
    /// whole point of deriving them.
    /// </remarks>
    [Fact]
    public void KnownKeysAreCompileTimeConstants()
    {
        Assert.Equal("common.add", MessageKey.Common.Add);
        Assert.Equal("common.cancel", MessageKey.Common.Cancel);
        Assert.Equal("terminal.clear", MessageKey.Terminal.Clear);
        Assert.Equal("chat.select", MessageKey.Chat.Select);

        // A key whose last part repeats its area: the type is `Feedback` and the
        // member cannot be, so the generator names it `KeyFeedback`.
        Assert.Equal("feedback.feedback", MessageKey.Feedback.KeyFeedback);
    }

    /// <summary>
    /// Checks that every table key has a constant whose value is that key.
    /// </summary>
    [Fact]
    public void EveryTableKeyHasAConstant()
    {
        var tableKeys = TableKeys();
        var constants = ConstantValues();

        var missing = tableKeys.Where(key => !constants.Contains(key)).OrderBy(key => key, StringComparer.Ordinal).ToList();
        var invented = constants.Where(key => !tableKeys.Contains(key)).OrderBy(key => key, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0, $"keys with no constant: {string.Join(", ", missing.Take(10))}");
        Assert.True(invented.Count == 0, $"constants with no key: {string.Join(", ", invented.Take(10))}");
    }

    /// <summary>
    /// Checks that the constants and the tables count the same messages, so a
    /// table that silently lost a chunk is caught here rather than as a missing
    /// sentence in the interface.
    /// </summary>
    [Fact]
    public void TheCountsAgree()
    {
        var tableKeys = TableKeys();
        var constants = ConstantValues();

        Assert.Equal(tableKeys.Count, constants.Count);
        Assert.True(tableKeys.Count > 5000, $"the tables hold only {tableKeys.Count} messages");
    }

    /// <summary>
    /// Checks that no two constants in one area carry the same key, which would
    /// mean the generator collapsed two distinct messages into one name.
    /// </summary>
    [Fact]
    public void EachAreaNamesEachKeyOnce()
    {
        foreach (var area in typeof(MessageKey).GetNestedTypes(BindingFlags.Public))
        {
            var values = area
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Select(field => (string)field.GetRawConstantValue()!)
                .ToList();

            Assert.Equal(values.Count, values.Distinct(StringComparer.Ordinal).Count());
        }
    }

    /// <summary>
    /// Reads every key from every rendered message table.
    /// </summary>
    /// <returns>The distinct keys the tables hold.</returns>
    /// <remarks>
    /// The tables are internal, so this assembly reads them through the
    /// <c>InternalsVisibleTo</c> the Pages project declares. Each table is an
    /// <see cref="IReadOnlyDictionary{TKey,TValue}"/> the generator wrote; its
    /// keys are the contract, so they are read reflectively rather than through a
    /// second list someone would have to keep current.
    /// </remarks>
    private static HashSet<string> TableKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var assembly = typeof(MessageKey).Assembly;

        foreach (var type in assembly.GetTypes().Where(t => t.IsAbstract && t.IsSealed && t.Name.StartsWith("Messages", StringComparison.Ordinal)))
        {
            var property = type.GetProperty("Table", BindingFlags.Public | BindingFlags.Static);
            if (property?.GetValue(null) is not IEnumerable rows)
            {
                continue;
            }

            foreach (var row in rows)
            {
                if (row is not null && row.GetType().GetProperty("Key")?.GetValue(row) is string key)
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }

    /// <summary>
    /// Reads every constant the generator emitted.
    /// </summary>
    /// <returns>The values the constants carry.</returns>
    private static HashSet<string> ConstantValues()
    {
        var values = new HashSet<string>(StringComparer.Ordinal);

        foreach (var area in typeof(MessageKey).GetNestedTypes(BindingFlags.Public))
        {
            foreach (var field in area.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.IsLiteral && field.FieldType == typeof(string))
                {
                    values.Add((string)field.GetRawConstantValue()!);
                }
            }
        }

        return values;
    }
}
