using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Reasonix.Studio.MessageKeys;

/// <summary>
/// Derives a compile-time constant for every message key the renderer produced.
/// </summary>
/// <remarks>
/// The tables are already generated data; what this adds is a name the compiler
/// checks. A call site then writes <c>MessageKey.Common.Add</c> and the build
/// fails when that key leaves the table, instead of the interface silently
/// rendering the key itself at run time.
///
/// The keys are read from the generated tables in <c>AdditionalFiles</c> rather
/// than from the compilation's syntax trees: a generator that read its own
/// project's sources would be re-parsing what the compiler has already parsed,
/// and the file list is the one thing MSBuild states explicitly.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class MessageKeyGenerator : IIncrementalGenerator
{
    /// <summary>
    /// One entry in a generated table, as the renderer writes it.
    /// </summary>
    private static readonly Regex s_entry =
        new Regex("^\\s*\\[\"(?<key>[^\"]+)\"\\] = \\((?<zh>.*)\\)\\s*,?\\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The area a key names, which is the part before its first dot.
    /// </summary>
    private const char Separator = '.';

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var tables = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            .Where(static file => file.Path.Replace('\\', '/').Contains("/I18n/", StringComparison.OrdinalIgnoreCase))
            .Select(static (file, token) => file.GetText(token))
            .Where(static text => text is not null);

        var collected = tables.Collect();

        context.RegisterSourceOutput(collected, static (output, texts) =>
        {
            var entries = ReadEntries(texts);
            if (entries.Count == 0)
            {
                return;
            }

            foreach (var file in Render(entries))
            {
                output.AddSource(file.Hint, SourceText.From(file.Text, Encoding.UTF8));
            }
        });
    }

    /// <summary>
    /// Reads every key from the generated tables.
    /// </summary>
    /// <param name="texts">The tables, as the compiler read them.</param>
    /// <returns>Keys, sorted, one per distinct message.</returns>
    private static List<string> ReadEntries(IEnumerable<SourceText?> texts)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var text in texts)
        {
            if (text is null)
            {
                continue;
            }

            foreach (var line in text.Lines)
            {
                var match = s_entry.Match(line.ToString());
                if (match.Success)
                {
                    keys.Add(match.Groups["key"].Value);
                }
            }
        }

        return keys.ToList();
    }

    /// <summary>
    /// Turns a message key into the name of its constant.
    /// </summary>
    /// <param name="key">The key, as the table spells it.</param>
    /// <returns>A PascalCase identifier made of the key's parts.</returns>
    /// <remarks>
    /// The name is derived rather than copied because a key holds characters C#
    /// forbids in an identifier. Two keys that derive the same name are told
    /// apart by the caller, which appends their position once it has the whole
    /// area in hand.
    /// </remarks>
    private static string NameOf(string key)
    {
        var builder = new StringBuilder(key.Length);
        var upper = true;
        foreach (var c in key)
        {
            if (c == Separator || c == '_' || c == '-' || c == ' ')
            {
                upper = true;
                continue;
            }

            if (!char.IsLetterOrDigit(c))
            {
                upper = true;
                continue;
            }

            builder.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }

        if (builder.Length == 0)
        {
            return "Message";
        }

        if (char.IsDigit(builder[0]))
        {
            builder.Insert(0, 'N');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Groups the keys by area and renders one file per group.
    /// </summary>
    /// <param name="keys">Every key, sorted.</param>
    /// <returns>One hint name and source per area.</returns>
    private static IEnumerable<(string Hint, string Text)> Render(IReadOnlyList<string> keys)
    {
        var areas = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var at = key.IndexOf(Separator);
            var area = at > 0 ? key.Substring(0, at) : "misc";
            if (!areas.TryGetValue(area, out var list))
            {
                areas[area] = list = new List<string>();
            }

            list.Add(key);
        }

        foreach (var area in areas)
        {
            yield return (HintOf(area.Key), TextOf(area.Key, area.Value));
        }
    }

    /// <summary>
    /// Names the file an area's constants go in.
    /// </summary>
    /// <param name="area">The area's name.</param>
    /// <returns>A hint name ending in <c>.g.cs</c>.</returns>
    private static string HintOf(string area)
    {
        return "MessageKeys." + TypeOf(area) + ".g.cs";
    }

    /// <summary>
    /// Turns an area into the name of its nested class.
    /// </summary>
    /// <param name="area">The area's name.</param>
    /// <returns>A PascalCase identifier.</returns>
    private static string TypeOf(string area)
    {
        var name = NameOf(area);
        return char.IsDigit(name[0]) ? "Area" + name : name;
    }

    /// <summary>
    /// Renders one area's constants.
    /// </summary>
    /// <param name="area">The area's name.</param>
    /// <param name="keys">Its keys, sorted.</param>
    /// <returns>The file's text.</returns>
    private static string TextOf(string area, IReadOnlyList<string> keys)
    {
        var type = TypeOf(area);
        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        var lines = new List<string>
        {
            "// <auto-generated/>",
            "namespace Reasonix.Studio.Pages.I18n;",
            string.Empty,
            "/// <summary>",
            "/// Message keys for the <c>" + area + "</c> area.",
            "/// </summary>",
            "public static partial class MessageKey",
            "{",
            "    /// <summary>",
            "    /// The <c>" + area + "</c> area's constants.",
            "    /// </summary>",
            "    public static partial class " + type,
            "    {",
        };

        foreach (var key in keys)
        {
            var name = NameOf(key.Substring(key.IndexOf(Separator) + 1));

            // A member cannot share its enclosing type's name, and a key such as
            // `feedback.feedback` derives exactly that. Prefixing is the smallest
            // way to keep the key reachable under a name nobody has to remember:
            // the area already says "feedback", and this says which member it is.
            if (string.Equals(name, type, StringComparison.Ordinal))
            {
                name = "Key" + name;
            }

            if (used.TryGetValue(name, out var seen))
            {
                // Two keys differing only in punctuation derive one name; the
                // suffix keeps both reachable and the order stable, because the
                // keys arrived sorted.
                used[name] = seen + 1;
                name = name + "_" + (seen + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                used[name] = 0;
            }

            lines.Add("        /// <summary>");
            lines.Add("        /// The key <c>" + key + "</c>.");
            lines.Add("        /// </summary>");
            lines.Add("        public const string " + name + " = \"" + key + "\";");
        }

        lines.Add("    }");
        lines.Add("}");
        lines.Add(string.Empty);
        return string.Join("\n", lines);
    }
}
