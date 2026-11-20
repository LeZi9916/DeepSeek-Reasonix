using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the message lookup to its two promises: a known key reads as the
/// reader's language, and an unknown one is reported without breaking the page.
/// </summary>
public sealed class MessageCatalogTests
{
    /// <summary>
    /// Checks that a known key reads in each language's own words.
    /// </summary>
    [Fact]
    public void AKnownKeyReadsInTheReadersLanguage()
    {
        Assert.Equal("添加", MessageCatalog.Get("common.add", InterfaceLanguage.Zh));
        Assert.Equal("Add", MessageCatalog.Get("common.add", InterfaceLanguage.En));
    }

    /// <summary>
    /// Checks that an unknown key comes back as itself rather than throwing.
    /// </summary>
    /// <remarks>
    /// A window that lost a phrase still draws: the reader sees a name they can
    /// report, and the session stays usable. Throwing would turn a wording gap
    /// into a blank page.
    /// </remarks>
    [Fact]
    public void AnUnknownKeyComesBackAsItself()
    {
        MessageCatalog.ResetDiagnostics();

        var text = MessageCatalog.Get("no.such_key", InterfaceLanguage.En);

        Assert.Equal("no.such_key", text);
        Assert.Contains("no.such_key", MessageCatalog.MissingKeys());
    }

    /// <summary>
    /// Checks that a miss is recorded once per key, not once per render.
    /// </summary>
    [Fact]
    public void AMissIsRecordedOncePerKey()
    {
        MessageCatalog.ResetDiagnostics();

        for (var i = 0; i < 10; i++)
        {
            MessageCatalog.Get("repeat.miss", InterfaceLanguage.Zh);
        }

        Assert.Single(MessageCatalog.MissingKeys());
    }

    /// <summary>
    /// Checks that the diagnostic list is bounded, and that the misses beyond it
    /// are still counted.
    /// </summary>
    /// <remarks>
    /// A page that renders an unknown key per row would otherwise grow the list
    /// for as long as the window is open. The count is what keeps a report able
    /// to say how bad it is without the list being the memory.
    /// </remarks>
    [Fact]
    public void TheDiagnosticListIsBoundedAndCountsWhatItDrops()
    {
        MessageCatalog.ResetDiagnostics();

        for (var i = 0; i < 300; i++)
        {
            MessageCatalog.Get("overflow." + i.ToString(System.Globalization.CultureInfo.InvariantCulture), InterfaceLanguage.En);
        }

        Assert.True(MessageCatalog.MissingKeys().Count <= 256, "the recorded list grew past its bound");
        Assert.True(MessageCatalog.SuppressedMisses > 0, "misses beyond the bound were not counted");
    }

    /// <summary>
    /// Checks that a placeholder takes the value it was given.
    /// </summary>
    [Fact]
    public void APlaceholderTakesItsValue()
    {
        var text = MessageCatalog.Get("common.address_completed_to_url", InterfaceLanguage.En, ("url", "http://127.0.0.1:1/"));

        Assert.Equal("Address completed to http://127.0.0.1:1/", text);
    }

    /// <summary>
    /// Checks that a placeholder with no value stays as written, so a caller that
    /// forgot one produces a visibly incomplete sentence rather than one that
    /// reads as complete and says the wrong thing.
    /// </summary>
    [Fact]
    public void AnUnfilledPlaceholderStaysAsWritten()
    {
        var text = MessageCatalog.Get("common.address_completed_to_url", InterfaceLanguage.Zh);

        Assert.Contains("{url}", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks that asking whether a key exists does not itself record a miss.
    /// </summary>
    [Fact]
    public void AskingWhetherAKeyExistsIsNotAMiss()
    {
        MessageCatalog.ResetDiagnostics();

        Assert.True(MessageCatalog.Has("common.add"));
        Assert.False(MessageCatalog.Has("not.a.real_key"));
        Assert.Empty(MessageCatalog.MissingKeys());
    }

    /// <summary>
    /// Checks that every key in the merged table answers in both languages, which
    /// is the one property the per-area tables promise by construction.
    /// </summary>
    [Fact]
    public void EveryTableKeyAnswersInBothLanguages()
    {
        MessageCatalog.ResetDiagnostics();

        var empty = MessageTables.All
            .Where(entry => string.IsNullOrWhiteSpace(entry.Value.Zh) || string.IsNullOrWhiteSpace(entry.Value.En))
            .Select(entry => entry.Key)
            .Take(10)
            .ToList();

        Assert.True(empty.Count == 0, $"keys with an empty side: {string.Join(", ", empty)}");
        Assert.Empty(MessageCatalog.MissingKeys());
    }
}
