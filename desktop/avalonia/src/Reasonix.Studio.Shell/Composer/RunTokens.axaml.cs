using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages.I18n;
using System;

namespace Reasonix.Studio.Shell.Composer;

/// <summary>Displays the current streamed token totals with the reference arrival feedback.</summary>
internal sealed partial class RunTokens : UserControl
{
    /// <summary>Identifies the last conversation projected into this readout.</summary>
    private (object? Owner, string? Session) _source;

    /// <summary>The last displayed counts, independent of locale and abbreviation.</summary>
    private (long Sent, long Received) _previous;

    /// <summary>The timeout for the outgoing feedback flag.</summary>
    private IDisposable? _sentTimer;

    /// <summary>The timeout for the incoming feedback flag.</summary>
    private IDisposable? _receivedTimer;

    /// <summary>Initializes a new instance of the <see cref="RunTokens"/> class.</summary>
    public RunTokens() => InitializeComponent();

    /// <summary>Updates the counts, marking only arrivals within the same conversation.</summary>
    /// <param name="source">Owner and session of the live counts.</param>
    /// <param name="sent">Tokens sampled at the provider boundary.</param>
    /// <param name="received">Billed output plus the current estimate.</param>
    /// <param name="estimated">Whether the current reading includes unbilled output.</param>
    /// <param name="language">Interface language.</param>
    /// <param name="motion">Whether the current update should show feedback.</param>
    internal void Show((object Owner, string Session) source, long sent, long received, bool estimated, InterfaceLanguage language, bool motion)
    {
        var same = ReferenceEquals(_source.Owner, source.Owner) && _source.Session == source.Session;
        if (!same || !motion)
        {
            CancelFeedback();
        }

        SentValue.Text = Format.Tokens(sent, language);
        ReceivedValue.Text = Format.Tokens(received, language);
        Received.Classes.Set("estimated", estimated);
        TokenUnit.Text = estimated ? "tokens ≈" : "tokens";
        ToolTip.SetTip(this, MessageCatalog.Get(
            estimated ? MessageKey.Composer.TokensSentThisTurnArriving : MessageKey.Composer.TokensSentAndReceivedThis, language));

        if (same && motion)
        {
            if (_previous.Sent != sent)
            {
                Land(Sent, ref _sentTimer);
            }

            if (_previous.Received != received)
            {
                Land(Received, ref _receivedTimer);
            }
        }

        _source = source;
        _previous = (sent, received);
    }

    /// <summary>Discards timed feedback while retaining the current reading.</summary>
    internal void CancelFeedback()
    {
        _sentTimer?.Dispose();
        _receivedTimer?.Dispose();
        _sentTimer = _receivedTimer = null;
        Sent.Classes.Remove("landed");
        Received.Classes.Remove("landed");
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelFeedback();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Keeps feedback active until the current burst goes quiet.</summary>
    /// <param name="reading">The changed token reading.</param>
    /// <param name="timer">The timeout previously attached to this reading.</param>
    private static void Land(StackPanel reading, ref IDisposable? timer)
    {
        reading.Classes.Set("landed", true);
        timer?.Dispose();
        timer = DispatcherTimer.RunOnce(() => reading.Classes.Remove("landed"), TimeSpan.FromMilliseconds(520));
    }
}
