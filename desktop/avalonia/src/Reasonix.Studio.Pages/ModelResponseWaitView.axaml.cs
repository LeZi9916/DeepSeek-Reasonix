using Avalonia.Controls;
using Avalonia.Threading;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State;
using System;

namespace Reasonix.Studio.Pages;

/// <summary>The transcript's provider-response wait and retry notice.</summary>
internal sealed partial class ModelResponseWaitView : UserControl
{
    /// <summary>Refreshes the label independently of the dot animation.</summary>
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(100) };

    /// <summary>The provider attempt currently shown.</summary>
    private ModelResponseWait? _wait;

    /// <summary>The reader's language.</summary>
    private InterfaceLanguage _language;

    /// <summary>Initializes a new instance of the <see cref="ModelResponseWaitView"/> class.</summary>
    public ModelResponseWaitView()
    {
        InitializeComponent();
        _clock.Tick += (_, _) =>
        {
            if (IsEffectivelyVisible)
            {
                Draw();
            }
        };
        AttachedToVisualTree += (_, _) => RefreshClock();
        DetachedFromVisualTree += (_, _) => _clock.Stop();
    }

    /// <summary>Shows a new attempt while retaining the running dots across retry updates.</summary>
    /// <param name="wait">The accepted provider wait.</param>
    /// <param name="language">The reader's language.</param>
    internal void Show(ModelResponseWait? wait, InterfaceLanguage language)
    {
        if (_wait == wait && _language == language)
        {
            return;
        }

        _wait = wait;
        _language = language;
        IsVisible = wait is not null;
        Classes.Set("retry", wait?.IsRetry == true);
        RefreshClock();
    }

    /// <summary>Stops the attempt clock when there is no active response wait.</summary>
    private void RefreshClock()
    {
        if (_wait is null)
        {
            _clock.Stop();
            return;
        }

        Draw();
        _clock.Start();
    }

    /// <summary>Displays elapsed time from the frame, independently of layout and theme changes.</summary>
    private void Draw()
    {
        if (_wait is not { } wait)
        {
            return;
        }

        var elapsed = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - wait.Since);
        if (!wait.IsRetry)
        {
            Label.Text = MessageCatalog.Get("composer.waiting_for_a_response_secs", _language, ("secs", Format.Decimals(elapsed / 1000d, 1, _language)));
            return;
        }

        var lead = wait.Cause switch
        {
            "connection_closed" => "composer.connection_closed",
            "timeout" => "composer.the_server_did_not_answer",
            "upstream_status" => "composer.server_returned_http_status",
            "stream_idle" => "composer.the_response_stalled",
            "upstream_error" => "composer.the_server_reported_an_error",
            _ => wait.Scope switch
            {
                "stream" => "composer.the_response_was_cut_mid",
                "headers" => "composer.the_request_did_not_succeed",
                _ => "composer.connection_dropped",
            },
        };
        var detail = MessageCatalog.Get(lead, _language, ("status", Format.Count(wait.Status, _language)));
        Label.Text = detail + " · " + RetryPhase(wait, elapsed);
    }

    /// <summary>Translates the kernel's backoff and the subsequent response timer.</summary>
    /// <param name="wait">The current retry envelope.</param>
    /// <param name="elapsed">Milliseconds since the kernel reported it.</param>
    /// <returns>The localized phase text.</returns>
    private string RetryPhase(ModelResponseWait wait, long elapsed)
    {
        var attempt = Format.Count(wait.Attempt, _language);
        var max = wait.Max > 0 ? Format.Count(wait.Max, _language) : "∞";
        var replay = wait.Scope == "stream";
        if (elapsed < wait.DelayMs)
        {
            var seconds = (long)Math.Ceiling((wait.DelayMs - elapsed) / 1000d);
            return MessageCatalog.Get(
                replay ? "composer.next_replay_attempt_max_in" : "composer.next_retry_attempt_max_in",
                _language,
                ("attempt", attempt),
                ("max", max),
                ("n", Format.Count(seconds, _language)));
        }

        return MessageCatalog.Get(
            replay ? "composer.replay_attempt_max_waiting_for" : wait.TimeoutSecs > 0 ? "composer.retry_attempt_max_waiting_for_2" : "composer.retry_attempt_max_waiting_for",
            _language,
            ("attempt", attempt),
            ("max", max),
            ("secs", Format.Decimals((elapsed - wait.DelayMs) / 1000d, 1, _language)),
            ("limit", Format.Count(wait.TimeoutSecs, _language)));
    }
}
