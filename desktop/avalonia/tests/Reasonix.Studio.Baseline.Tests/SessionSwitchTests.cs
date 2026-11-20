using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.Session;
using System.Collections.Generic;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the two rules a session switch has: which open wins when several are in
/// flight, and which boundary a refusal names.
/// </summary>
/// <remarks>
/// Both are places where a plausible-looking shortcut is wrong. Focus that
/// follows whichever answer lands last shows the conversation the person
/// abandoned; a refusal classified by its sentence paints "gone" over "held
/// elsewhere". The cases here pin the ticket ordering and the status-and-code
/// mapping separately, because they fail independently.
/// </remarks>
public sealed class SessionSwitchTests
{
    /// <summary>
    /// Checks that only the latest ticket is current, and that an older one
    /// finishing late leaves the newer selection alone.
    /// </summary>
    [Fact]
    public void OnlyTheLatestOpenIsCurrent()
    {
        var sw = new SessionSwitch();
        var first = sw.Begin("/a.jsonl");
        var second = sw.Begin("/b.jsonl");

        Assert.False(sw.IsCurrent(first));
        Assert.True(sw.IsCurrent(second));
        Assert.Equal("/b.jsonl", sw.Path);

        // The older open finishing must not clear the newer one's mark.
        sw.End(first);
        Assert.Equal("/b.jsonl", sw.Path);

        sw.End(second);
        Assert.Equal(string.Empty, sw.Path);
    }

    /// <summary>
    /// Checks that a second click on the row already opening is the same request.
    /// </summary>
    [Fact]
    public void TheRowAlreadyOpeningIsNotOpenedTwice()
    {
        var sw = new SessionSwitch();
        sw.Begin(@"C:\work\a.jsonl");

        Assert.True(sw.Opening(@"c:\WORK\a.jsonl"));
        Assert.False(sw.Opening(@"C:\work\b.jsonl"));

        // A new session has no row, so it is never "already opening".
        sw.Begin(string.Empty);
        Assert.False(sw.Opening(string.Empty));
    }

    /// <summary>
    /// Checks that cancelling forgets whatever was opening.
    /// </summary>
    [Fact]
    public void CancellingForgetsTheOpen()
    {
        var sw = new SessionSwitch();
        var ticket = sw.Begin("/a.jsonl");
        sw.Cancel();

        Assert.False(sw.IsCurrent(ticket));
        Assert.Equal(string.Empty, sw.Path);
    }

    /// <summary>
    /// Checks that a coded refusal is read by its code, and a status-only refusal
    /// by its status.
    /// </summary>
    [Fact]
    public void EachBoundaryIsReadFromTheIdentityItSent()
    {
        Assert.Equal(SessionBoundary.Gone, SessionBoundaries.Of(404, "session.unknown"));
        Assert.Equal(SessionBoundary.TooLarge, SessionBoundaries.Of(413, "session.replay_limit"));
        Assert.Equal(SessionBoundary.Invalid, SessionBoundaries.Of(400, "session.bad_path"));
        Assert.Equal(SessionBoundary.Outside, SessionBoundaries.Of(403, "session.outside_dir"));
        Assert.Equal(SessionBoundary.Held, SessionBoundaries.Of(409, "busy.session_in_use"));
    }

    /// <summary>
    /// Checks that the code wins where a status alone would be ambiguous: the
    /// kernel answers 400 for a bad name and for a name that is not a transcript.
    /// </summary>
    [Fact]
    public void TheCodeRefinesAnAmbiguousStatus()
    {
        Assert.Equal(SessionBoundary.Outside, SessionBoundaries.Of(400, "session.outside_dir"));
        Assert.Equal(SessionBoundary.Invalid, SessionBoundaries.Of(400, null));
    }

    /// <summary>
    /// Checks that a plain-text refusal — the 409 a running turn answers with —
    /// is still read as the boundary it is.
    /// </summary>
    [Fact]
    public void AStatusOnlyRefusalIsStillABoundary()
    {
        Assert.Equal(SessionBoundary.Held, SessionBoundaries.Of(409, null));
        Assert.Equal(SessionBoundary.Gone, SessionBoundaries.Of(404, string.Empty));
        Assert.Equal(SessionBoundary.Unknown, SessionBoundaries.Of(500, null));
    }

    /// <summary>
    /// Checks that a refusal carrying nothing at all is unknown rather than
    /// guessed at.
    /// </summary>
    [Fact]
    public void ARefusalWithNoIdentityIsUnknown()
    {
        Assert.Equal(SessionBoundary.Unknown, SessionBoundaries.Of(null!));
    }

    /// <summary>
    /// Checks the reading a client hands over: the boundary is what a switch
    /// refusal is, not the sentence it arrived in.
    /// </summary>
    [Fact]
    public void AClientRefusalKeepsItsBoundary()
    {
        var held = new KernelRefusal("/resume", 409, null, "a turn is running on that conversation");
        var gone = new KernelRefusal(
            "/resume",
            404,
            new Reasonix.Studio.Contracts.Refusal.Reason { Code = "session.unknown", Error = "no such session" },
            null);

        Assert.Equal(SessionBoundary.Held, SessionBoundaries.Of(held));
        Assert.Equal(SessionBoundary.Gone, SessionBoundaries.Of(gone));
    }

    /// <summary>
    /// Checks that only "held" still describes the conversation on screen: every
    /// other boundary means the switch never landed.
    /// </summary>
    [Fact]
    public void OnlyAHeldSessionKeepsTheConversationOpen()
    {
        Assert.False(SessionBoundaries.KeepsTheOpenConversation(SessionBoundary.Held));

        foreach (var boundary in new[]
        {
            SessionBoundary.Gone,
            SessionBoundary.TooLarge,
            SessionBoundary.Invalid,
            SessionBoundary.Outside,
            SessionBoundary.Unknown,
        })
        {
            Assert.True(SessionBoundaries.KeepsTheOpenConversation(boundary));
        }
    }
}
