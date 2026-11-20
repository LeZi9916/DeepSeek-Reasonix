using Reasonix.Studio.Contracts.Queue;
using Reasonix.Studio.Pages.Composer;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the composer's rules on their own: which press sends, what a running
/// turn makes a line, where a draft lives, and what the meter reads.
/// </summary>
/// <remarks>
/// These are the decisions a composer makes before it talks to a kernel. Each one
/// has a failure a reader would feel: a send key that eats a newline, a steer
/// that becomes a stray turn, a draft that reappears in the wrong session, a
/// gauge that adds attempts together.
/// </remarks>
public sealed class ComposerTests
{
    /// <summary>
    /// Checks that in the plain mode Enter sends and Shift+Enter starts a line.
    /// </summary>
    [Fact]
    public void EnterSendsAndShiftEnterDoesNot()
    {
        Assert.True(SendKey.SendsOnKey(enter: true, shift: false, control: false, SendShortcut.Enter));
        Assert.False(SendKey.SendsOnKey(enter: true, shift: true, control: false, SendShortcut.Enter));
        Assert.False(SendKey.SendsOnKey(enter: false, shift: false, control: false, SendShortcut.Enter));
    }

    /// <summary>
    /// Checks that in the modifier mode a bare Enter belongs to the text, so an
    /// input method's own Enter never sends a half-written line.
    /// </summary>
    [Fact]
    public void ModifierModeLeavesPlainEnterToTheText()
    {
        Assert.False(SendKey.SendsOnKey(enter: true, shift: false, control: false, SendShortcut.ModifierEnter));
        Assert.True(SendKey.SendsOnKey(enter: true, shift: false, control: true, SendShortcut.ModifierEnter));

        // The chord must be exact: the modifier plus Shift is still a new line.
        Assert.False(SendKey.SendsOnKey(enter: true, shift: true, control: true, SendShortcut.ModifierEnter));
    }

    /// <summary>
    /// Checks that the hint names the keys in force and what the press will do.
    /// </summary>
    [Fact]
    public void TheHintNamesTheKeysAndTheOutcome()
    {
        Assert.Equal(
            "Enter 发送 · Shift+Enter 换行",
            SendKey.Hint(SendShortcut.Enter, running: false, InterfaceLanguage.Zh));
        Assert.Equal(
            "Enter 插话 · Shift+Enter 换行",
            SendKey.Hint(SendShortcut.Enter, running: true, InterfaceLanguage.Zh));
        Assert.Equal(
            "Enter 排队 · Shift+Enter 换行",
            SendKey.Hint(SendShortcut.Enter, running: true, InterfaceLanguage.Zh, SendKey.Intent.FollowUp));
        Assert.Equal(
            "Ctrl Enter to send · Enter for a new line",
            SendKey.Hint(SendShortcut.ModifierEnter, running: false, InterfaceLanguage.En));
    }

    /// <summary>
    /// Checks that a line sent while nothing runs is a follow-up, and that one
    /// sent while a turn runs keeps the preference.
    /// </summary>
    [Fact]
    public void ARunningTurnDecidesWhatTheLineBecomes()
    {
        Assert.Equal(SendKey.Intent.FollowUp, SendKey.IntentFor(running: false, SendKey.Intent.Steer));
        Assert.Equal(SendKey.Intent.Steer, SendKey.IntentFor(running: true, SendKey.Intent.Steer));
        Assert.Equal(SendKey.Intent.FollowUp, SendKey.IntentFor(running: true, SendKey.Intent.FollowUp));
    }

    /// <summary>
    /// Checks that the meter shows what the last request took against the window,
    /// and says nothing before either is known.
    /// </summary>
    [Fact]
    public void TheMeterShowsBothHalves()
    {
        Assert.Equal("—", ContextMeter.Readout(0, 0, InterfaceLanguage.Zh));
        Assert.Equal("1.2k / 128k", ContextMeter.Readout(1_200, 128_000, InterfaceLanguage.Zh));
        Assert.Equal("900", ContextMeter.Readout(900, 0, InterfaceLanguage.En));
    }

    /// <summary>
    /// Checks that a draft goes back to the session it was written in, and that
    /// two sessions never share a key.
    /// </summary>
    [Fact]
    public void ADraftBelongsToItsConversation()
    {
        var storage = new MemoryDrafts();
        var first = DraftStore.KeyFor("127.0.0.1:4100", @"C:\work", @"C:\work\.reasonix\a.jsonl");
        var second = DraftStore.KeyFor("127.0.0.1:4100", @"C:\work", @"C:\work\.reasonix\b.jsonl");

        Assert.NotEqual(first, second);
        Assert.Equal(string.Empty, DraftStore.KeyFor("127.0.0.1:4100", @"C:\work", string.Empty));

        DraftStore.Write(storage, first, "half a thought");
        Assert.Equal("half a thought", DraftStore.Read(storage, first));
        Assert.Equal(string.Empty, DraftStore.Read(storage, second));

        DraftStore.Write(storage, first, string.Empty);
        Assert.Equal(string.Empty, DraftStore.Read(storage, first));
    }

    /// <summary>
    /// Checks that an oversized draft is dropped rather than restored in part.
    /// </summary>
    [Fact]
    public void AnOversizedDraftIsDroppedWhole()
    {
        var storage = new MemoryDrafts();
        var key = DraftStore.KeyFor("h", "r", "p");
        var huge = new string('x', DraftStore.MaxBytes + 1);

        DraftStore.Write(storage, key, huge);

        Assert.Equal(string.Empty, DraftStore.Read(storage, key));
    }

    /// <summary>
    /// Checks that the meter reads the latest request's occupancy rather than the
    /// billable total, which sums every attempt a round needed.
    /// </summary>
    [Fact]
    public void TheFillPrefersTheLatestRequest()
    {
        Assert.Equal(122_000, ContextFill.Of(122_000, 400_000));
        Assert.Equal(400_000, ContextFill.Of(0, 400_000));
        Assert.Equal(0, ContextFill.Of(0, 0));
    }

    /// <summary>
    /// Checks that the strip draws only lines still waiting: an entry the kernel
    /// already took is in the transcript, and a second row for it would be a
    /// duplicate.
    /// </summary>
    [Fact]
    public void TakenLinesLeaveTheStrip()
    {
        var queue = new QueueSnapshot
        {
            Items = new[]
            {
                new QueueItem { Id = "a", Intent = "followup", State = "queued", Preview = "waiting" },
                new QueueItem { Id = "b", Intent = "steer", State = "steer_consumed", Preview = "already said" },
                new QueueItem { Id = "c", Intent = "followup", State = "running", Preview = "now a turn" },
            },
        };

        var rows = QueuePresenter.Rows(queue, running: true, InterfaceLanguage.Zh);

        Assert.Equal(new[] { "a" }, rows.Select(row => row.Id));
    }

    /// <summary>
    /// Checks that a host continuation is drawn only when it needs a decision:
    /// it belongs with the work it came from, not with the person's lines.
    /// </summary>
    [Fact]
    public void AHostContinuationShowsOnlyWhenItNeedsADecision()
    {
        var queue = new QueueSnapshot
        {
            Items = new[]
            {
                new QueueItem { Id = "h1", Intent = "followup", State = "queued", Origin = "host", Preview = "pick this back up" },
                new QueueItem { Id = "h2", Intent = "followup", State = "blocked", Origin = "host", Preview = "blocked job" },
                new QueueItem { Id = "h3", Intent = "followup", State = "uncertain", Origin = "host", Preview = "unclear job" },
            },
        };

        var rows = QueuePresenter.Rows(queue, running: false, InterfaceLanguage.Zh);

        Assert.Equal(new[] { "h2", "h3" }, rows.Select(row => row.Id));
    }

    /// <summary>
    /// Checks that only a plain text follow-up offers to become guidance: a
    /// picture or a frozen file travels with a whole turn.
    /// </summary>
    [Fact]
    public void OnlyPlainTextCanBecomeGuidance()
    {
        var queue = new QueueSnapshot
        {
            Items = new[]
            {
                new QueueItem { Id = "plain", Intent = "followup", State = "queued" },
                new QueueItem { Id = "image", Intent = "followup", State = "queued", Images = true },
                new QueueItem { Id = "file", Intent = "followup", State = "queued", Refs = new[] { new QueueRef { Path = "a.go" } } },
                new QueueItem { Id = "already", Intent = "steer", State = "queued" },
            },
        };

        var rows = QueuePresenter.Rows(queue, running: true, InterfaceLanguage.Zh);

        Assert.True(rows.Single(row => row.Id == "plain").CanSteer);
        Assert.False(rows.Single(row => row.Id == "image").CanSteer);
        Assert.False(rows.Single(row => row.Id == "file").CanSteer);
        Assert.False(rows.Single(row => row.Id == "already").CanSteer);
    }

    /// <summary>
    /// Checks that a retry is offered only where the kernel knows the line never
    /// reached the model, because the other uncertain lines may have run.
    /// </summary>
    [Fact]
    public void RetryIsOfferedOnlyWhereItCannotRepeat()
    {
        Assert.True(QueuePresenter.Retryable(new QueueItem { State = "blocked" }));
        Assert.True(QueuePresenter.Retryable(new QueueItem { State = "uncertain", BlockCode = "steer_unapplied" }));
        Assert.False(QueuePresenter.Retryable(new QueueItem { State = "uncertain", BlockCode = "turn_record_failed" }));
        Assert.False(QueuePresenter.Retryable(new QueueItem { State = "queued" }));
    }

    /// <summary>
    /// Checks that a queue with nothing waiting draws no strip, which is the state
    /// the baseline measured and the one most windows are in.
    /// </summary>
    [Fact]
    public void AnEmptyQueueDrawsNothing()
    {
        Assert.Empty(QueuePresenter.Rows(null, running: false, InterfaceLanguage.Zh));
        Assert.Empty(QueuePresenter.Rows(new QueueSnapshot(), running: true, InterfaceLanguage.Zh));
        Assert.False(QueuePresenter.OffersRelease(null));
        Assert.False(QueuePresenter.OffersRelease(new QueueSnapshot()));
        Assert.True(QueuePresenter.OffersRelease(new QueueSnapshot { Paused = true }));
    }

    /// <summary>
    /// Checks that a waiting follow-up is named by its place in the queue, and a
    /// steer by what it is.
    /// </summary>
    [Fact]
    public void ALabelSaysWhatTheLineIs()
    {
        var rows = QueuePresenter.Rows(
            new QueueSnapshot
            {
                Items = new[]
                {
                    new QueueItem { Id = "a", Intent = "followup", State = "queued", Preview = "first" },
                    new QueueItem { Id = "b", Intent = "steer", State = "queued", Preview = "second" },
                    new QueueItem { Id = "c", Intent = "followup", State = "blocked", Preview = "third", BlockReason = "the file was deleted" },
                },
            },
            running: true,
            InterfaceLanguage.Zh);

        Assert.Equal("排队 1", rows[0].Label);
        Assert.Equal("插话排队", rows[1].Label);
        Assert.Equal("受阻", rows[2].Label);
        Assert.Equal("the file was deleted", rows[2].Why);
    }

    /// <summary>
    /// Keeps drafts in memory where a case can read them back.
    /// </summary>
    private sealed class MemoryDrafts : IDraftStorage
    {
        /// <summary>
        /// The drafts, by key.
        /// </summary>
        private readonly Dictionary<string, string> _drafts = new Dictionary<string, string>();

        /// <inheritdoc/>
        public string Read(string key)
        {
            return _drafts.TryGetValue(key, out var text) ? text : string.Empty;
        }

        /// <inheritdoc/>
        public void Write(string key, string text)
        {
            if (text.Length == 0)
            {
                _drafts.Remove(key);
                return;
            }

            _drafts[key] = text;
        }
    }
}
