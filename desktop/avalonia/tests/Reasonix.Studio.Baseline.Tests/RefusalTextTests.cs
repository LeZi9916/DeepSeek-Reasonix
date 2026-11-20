using Reasonix.Studio.Contracts.Refusal;
using Reasonix.Studio.Pages.I18n;
using System.Text.Json;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds a refusal's code to the sentence this interface has for it.
/// </summary>
/// <remarks>
/// The kernel names what happened; which words a reader sees is this window's
/// decision. The cases here are the four ways a refusal can arrive, not one per
/// code: a code with wording, a code with a value to fill in, a code this build
/// has never heard of, and an answer that carried no code at all.
/// </remarks>
public sealed class RefusalTextTests
{
    /// <summary>
    /// Checks that a code the interface knows is said in the reader's language,
    /// which is the whole reason the kernel sends a code instead of a sentence.
    /// </summary>
    [Fact]
    public void AKnownCodeIsSaidInTheReadersLanguage()
    {
        var reason = new Reason { Code = "session.unknown", Error = "no such session" };

        Assert.Equal("没有这个会话", RefusalText.Say(reason, 404, null, InterfaceLanguage.Zh));
        Assert.Equal("No such conversation", RefusalText.Say(reason, 404, null, InterfaceLanguage.En));
    }

    /// <summary>
    /// Checks that the values the kernel sent fill the sentence it is said in.
    /// </summary>
    [Fact]
    public void AParamFillsTheSentence()
    {
        var reason = JsonSerializer.Deserialize<Reason>(
            "{\"code\":\"request.missing_field\",\"error\":\"missing path\",\"params\":{\"field\":\"path\"}}");

        Assert.Equal("缺少「path」", RefusalText.Say(reason, 400, null, InterfaceLanguage.Zh));
    }

    /// <summary>
    /// Checks that a code this build has no wording for degrades to the kernel's
    /// English rather than to a blank, because an untranslated message is still
    /// better than none.
    /// </summary>
    [Fact]
    public void AnUnknownCodeDegradesToTheKernelsEnglish()
    {
        var reason = new Reason { Code = "session.brand_new_code", Error = "something the shell has not seen" };

        Assert.Equal(
            "something the shell has not seen",
            RefusalText.Say(reason, 500, null, InterfaceLanguage.Zh));
    }

    /// <summary>
    /// Checks that a 409 from <c>/resume</c> — which the kernel writes as plain
    /// text, with no code — keeps that text instead of becoming "HTTP 409".
    /// </summary>
    [Fact]
    public void APlainTextRefusalKeepsItsAccount()
    {
        Assert.Equal(
            "a turn is running on this conversation",
            RefusalText.Say(null, 409, "a turn is running on this conversation", InterfaceLanguage.Zh));
    }

    /// <summary>
    /// Checks that an answer with nothing but a status is still a sentence, and
    /// that it names the status rather than a path and a number.
    /// </summary>
    [Fact]
    public void AStatusAloneIsStillASentence()
    {
        Assert.Equal("请求未能送达内核（HTTP 413）", RefusalText.Say(null, 413, null, InterfaceLanguage.Zh));
        Assert.Equal(
            "That request never reached the kernel (HTTP 413)",
            RefusalText.Say(null, 413, null, InterfaceLanguage.En));
    }
}
