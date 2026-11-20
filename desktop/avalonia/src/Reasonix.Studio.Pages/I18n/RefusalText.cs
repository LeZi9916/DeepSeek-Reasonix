using Reasonix.Studio.Contracts.Refusal;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Reasonix.Studio.Pages.I18n;

/// <summary>
/// Turns what the kernel refused into the sentence a reader sees.
/// </summary>
/// <remarks>
/// The kernel answers with a status and, when it can, a dotted code; which
/// sentence to show is this window's decision. Ported from the reference
/// frontend's <c>i18n/kernel.ts</c>: a code this build has wording for is said in
/// the reader's language, an unknown code degrades to the kernel's English, and a
/// plain-text refusal keeps the text it carried. A status alone is still a
/// sentence — "never reached the kernel (HTTP 409)" — because that is the only
/// thing a reader can act on when the body was empty.
/// </remarks>
public static class RefusalText
{
    /// <summary>
    /// Reads the sentence for one refusal.
    /// </summary>
    /// <param name="reason">The coded refusal, or null when the answer carried none.</param>
    /// <param name="status">The HTTP status, or null when the answer never had one.</param>
    /// <param name="detail">The plain-text account, or null when there was none.</param>
    /// <param name="language">The language to say it in.</param>
    /// <returns>The sentence, in the best identity the refusal carried.</returns>
    public static string Say(Reason? reason, int? status, string? detail, InterfaceLanguage language)
    {
        if (reason?.Code is { Length: > 0 } code && RefusalSaid.ByCode.TryGetValue(code, out var key))
        {
            return MessageCatalog.Get(key, language, Values(reason));
        }

        if (!string.IsNullOrWhiteSpace(reason?.Error))
        {
            return reason.Error;
        }

        if (!string.IsNullOrWhiteSpace(detail))
        {
            return detail;
        }

        if (status is { } number)
        {
            return MessageCatalog.Get(
                MessageKey.Kernel.ThatRequestNeverReachedThe,
                language,
                ("status", number.ToString(CultureInfo.InvariantCulture)));
        }

        return MessageCatalog.Get(MessageKey.Kernel.ThatRequestCouldNotBe, language);
    }

    /// <summary>
    /// Reads a coded refusal's parameters as the text a sentence fills in.
    /// </summary>
    /// <param name="reason">The coded refusal, or null.</param>
    /// <returns>The values by name; empty when the refusal carried none.</returns>
    public static IReadOnlyDictionary<string, string> Values(Reason? reason)
    {
        var values = new Dictionary<string, string>(reason?.Params?.Count ?? 0, StringComparer.Ordinal);
        if (reason?.Params is null)
        {
            return values;
        }

        foreach (var (name, element) in reason.Params)
        {
            values[name] = element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? string.Empty
                : element.ToString();
        }

        return values;
    }
}
