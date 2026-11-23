using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>A service endpoint and credential slot with one configured entry per protocol.</summary>
internal sealed partial class ProviderServiceAccount
{
    /// <summary>Initializes a new instance of the <see cref="ProviderServiceAccount"/> class.</summary>
    /// <param name="key">Stable service identifier.</param>
    /// <param name="host">Endpoint host with any non-default port.</param>
    private ProviderServiceAccount(string key, string host)
    {
        Key = key;
        Host = host;
    }

    /// <summary>Gets the stable host and credential identifier.</summary>
    internal string Key
    {
        get;
    }

    /// <summary>Gets the endpoint host.</summary>
    internal string Host
    {
        get;
    }

    /// <summary>Gets the user-facing account label.</summary>
    internal string Label
    {
        get;
        private set;
    } = string.Empty;

    /// <summary>Gets the provider entries indexed by protocol in configuration order.</summary>
    internal Dictionary<string, JsonObject> Entries
    {
        get;
    } = new(StringComparer.Ordinal);

    /// <summary>Gets the first configured provider name.</summary>
    internal string FirstName => Name(Entries.Values.First());

    /// <summary>Gets a value indicating whether any protocol is active in the conversation.</summary>
    internal bool InUse => Entries.Values.Any(entry => (bool?)entry["inUse"] == true);

    /// <summary>Gets the explicitly stored display name, if one was supplied.</summary>
    internal string? DisplayName => Entries.Values.Select(entry => ((string?)entry["displayName"])?.Trim())
        .FirstOrDefault(name => !string.IsNullOrEmpty(name));

    /// <summary>Gets a declared vendor mark from the account.</summary>
    internal string? Mark => Entries.Values.Select(entry => (string?)entry["mark"]).FirstOrDefault(mark => !string.IsNullOrEmpty(mark));

    /// <summary>Gets the derived fallback label when the custom display name is blank.</summary>
    internal string DefaultLabel => LabelForEntries(useDisplayName: false);

    /// <summary>Groups providers without losing duplicate entries for the same protocol.</summary>
    /// <param name="providers">Authoritative provider inventory.</param>
    /// <param name="order">Saved order of provider names.</param>
    /// <returns>Accounts in display order.</returns>
    internal static IReadOnlyList<ProviderServiceAccount> Group(JsonArray providers, IReadOnlyList<string> order)
    {
        var accounts = new Dictionary<string, ProviderServiceAccount>(StringComparer.Ordinal);
        foreach (var provider in providers.OfType<JsonObject>())
        {
            var address = (string?)provider["baseUrl"] ?? string.Empty;
            var host = Uri.TryCreate(address, UriKind.Absolute, out var url) && url.Host.Length > 0
                ? (url.IsDefaultPort ? url.Host : url.Authority).ToLowerInvariant()
                : address.Trim().ToLowerInvariant();
            var key = host + "\0" + ((string?)provider["keyEnv"] ?? string.Empty).Trim();
            var kind = Kind(provider);
            if (accounts.TryGetValue(key, out var account) && account.Entries.ContainsKey(kind))
            {
                key += "\0" + Name(provider);
            }

            if (!accounts.TryGetValue(key, out account))
            {
                account = new ProviderServiceAccount(key, host);
                accounts.Add(key, account);
            }

            account.Entries.Add(kind, provider);
        }

        var ranked = order.Select((name, index) => (name, index))
            .GroupBy(item => item.name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal);
        foreach (var account in accounts.Values)
        {
            account.Label = account.LabelForEntries();
        }

        foreach (var group in accounts.Values.GroupBy(account => (account.Host, account.Label)).Where(group => group.Skip(1).Any()))
        {
            foreach (var account in group.Where(account => account.FirstName != account.Label))
            {
                account.Label += " · " + account.FirstName;
            }
        }

        return accounts.Values.OrderBy(account => account.Entries.Values.Min(entry => ranked.GetValueOrDefault(Name(entry), int.MaxValue))).ToArray();
    }

    /// <summary>Returns the canonical configuration name for a protocol entry.</summary>
    /// <param name="entry">Provider response.</param>
    /// <returns>The configuration key.</returns>
    internal static string Name(JsonObject entry) => (string?)entry["name"] ?? string.Empty;

    /// <summary>Returns the protocol declared by a provider entry.</summary>
    /// <param name="entry">Provider response.</param>
    /// <returns>The protocol id.</returns>
    internal static string Kind(JsonObject entry) => (string?)entry["kind"] is { Length: > 0 } kind ? kind : "openai";

    /// <summary>Gets the service subdomains excluded from generated names.</summary>
    /// <returns>The host prefix expression.</returns>
    [GeneratedRegex("^(www|api|open|gateway)\\.", RegexOptions.CultureInvariant)]
    private static partial Regex HostPrefix();

    /// <summary>Gets characters that cannot be used in configuration names.</summary>
    /// <returns>The configuration-name expression.</returns>
    [GeneratedRegex("[^a-zA-Z0-9._-]", RegexOptions.CultureInvariant)]
    private static partial Regex NameCharacters();

    /// <summary>Gets the numeric suffix added to duplicate generated names.</summary>
    /// <returns>The numeric suffix expression.</returns>
    [GeneratedRegex("-[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex NameSuffix();

    /// <summary>Matches the name derivation used for the settings rail.</summary>
    /// <param name="useDisplayName">Whether to prefer the stored display name.</param>
    /// <returns>The service name.</returns>
    private string LabelForEntries(bool useDisplayName = true)
    {
        if (useDisplayName && DisplayName is { } display)
        {
            return display;
        }

        var bare = HostPrefix().Replace(Host, string.Empty).Split('.')[0];
        var safeName = NameCharacters().Replace(bare, "-");
        var automatic = safeName.Length == 0 ? "custom" : safeName;
        var chosen = Entries.Values.FirstOrDefault(entry => (bool?)entry["preset"] != true
            && !NameSuffix().Replace(Name(entry).Trim(), string.Empty).Equals(automatic, StringComparison.OrdinalIgnoreCase));
        return chosen is null ? bare : Name(chosen).Trim();
    }
}
