using Reasonix.Studio.Pages.I18n;

namespace Reasonix.Studio.Pages.Composer;

/// <summary>
/// Which key sends the line, which key starts a new one, and what the runs say
/// before either is pressed.
/// </summary>
/// <remarks>
/// Ported from the reference frontend's <c>ui/sendkey.ts</c>. The rule that
/// matters is that a modifier chord never steals the plain Enter: in
/// <see cref="SendShortcut.ModifierEnter"/> a bare Enter belongs to the text, so
/// a line being written is never sent by the keystroke that finishes a word.
/// </remarks>
public static class SendKey
{
    /// <summary>
    /// Gets the chord this platform spells its shortcuts with.
    /// </summary>
    /// <remarks>
    /// A constant rather than a reading of the runtime: this shell ships for the
    /// desktop, where the modifier is the one the window's own menus name.
    /// </remarks>
    public const string Chord = "Ctrl Enter";

    /// <summary>
    /// What the composer will do with the line while a turn is running.
    /// </summary>
    public enum Intent
    {
        /// <summary>
        /// Slip the line into the running turn at its next tool boundary.
        /// </summary>
        Steer,

        /// <summary>
        /// Queue the line behind the running turn.
        /// </summary>
        FollowUp,
    }

    /// <summary>
    /// Decides whether one press sends the line.
    /// </summary>
    /// <param name="enter">Whether the key was Enter.</param>
    /// <param name="shift">Whether Shift was held.</param>
    /// <param name="control">Whether the platform modifier was held.</param>
    /// <param name="mode">The preference in force.</param>
    /// <returns>True when this press sends rather than starts a new line.</returns>
    /// <remarks>
    /// In <see cref="SendShortcut.ModifierEnter"/> the chord must be exact: Enter
    /// with Shift as well is a new line, even with the modifier held.
    /// </remarks>
    public static bool SendsOnKey(bool enter, bool shift, bool control, SendShortcut mode)
    {
        if (!enter)
        {
            return false;
        }

        return mode == SendShortcut.ModifierEnter
            ? control && !shift
            : !shift;
    }

    /// <summary>
    /// Reads what the next press will do, in the reader's language.
    /// </summary>
    /// <param name="mode">The preference in force.</param>
    /// <param name="running">Whether a turn is running.</param>
    /// <param name="language">The language to say it in.</param>
    /// <param name="intent">What a line sent now would become.</param>
    /// <returns>The sentence under the line, naming the keys and the outcome.</returns>
    /// <remarks>
    /// Three outcomes, not two: while a turn runs, a line can steer it or queue
    /// behind it, and a hint that said "steer" over a line that will queue would
    /// misdescribe the next keystroke.
    /// </remarks>
    public static string Hint(
        SendShortcut mode,
        bool running,
        InterfaceLanguage language,
        Intent intent = Intent.Steer)
    {
        var queues = running && intent == Intent.FollowUp;
        if (mode == SendShortcut.ModifierEnter)
        {
            var key = queues
                ? MessageKey.Composer.KeyToQueueEnterFor
                : MessageKey.Composer.KeyToSendEnterFor;
            if (running && !queues)
            {
                key = MessageKey.Composer.KeyToSteerEnterFor;
            }

            return MessageCatalog.Get(key, language, ("key", Chord));
        }

        if (queues)
        {
            return MessageCatalog.Get(MessageKey.Composer.EnterToQueueShiftEnter, language);
        }

        return running
            ? MessageCatalog.Get(MessageKey.Composer.EnterToSteerShiftEnter, language)
            : MessageCatalog.Get(MessageKey.Composer.EnterToSendShiftEnter, language);
    }

    /// <summary>
    /// Decides what a line sent now becomes.
    /// </summary>
    /// <param name="running">Whether a turn is running.</param>
    /// <param name="preferred">The preference in force.</param>
    /// <returns>
    /// <see cref="Intent.FollowUp"/> when nothing is running, which queues the
    /// line and lets the kernel dispatch it as the next turn; otherwise the
    /// preference.
    /// </returns>
    public static Intent IntentFor(bool running, Intent preferred)
    {
        return running ? preferred : Intent.FollowUp;
    }
}
