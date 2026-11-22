using Avalonia.Controls;
using Avalonia.Threading;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Globalization;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Refreshes a billing countdown independently of whether a model is running.</summary>
    private readonly DispatcherTimer _tariffPulse = new() { Interval = TimeSpan.FromSeconds(30) };

    /// <summary>The active billing deadline for which a fresh canonical answer was requested.</summary>
    private string _tariffAsked = string.Empty;

    /// <summary>Shows the host's billing window and asks again once its declared deadline passes.</summary>
    private void DrawTariff()
    {
        var tariff = _composerStatus.Tariff;
        var valid = DateTimeOffset.TryParse(tariff?.ChangesAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var deadline);
        var left = valid ? deadline - DateTimeOffset.UtcNow : TimeSpan.Zero;
        var due = tariff is not null && valid && left <= TimeSpan.Zero;
        TariffMeter.IsVisible = tariff is not null && !due;
        if (tariff is null)
        {
            _tariffPulse.Stop();
            return;
        }

        var remaining = valid && !due ? TariffRemaining(left) : string.Empty;
        TariffText.Text = MessageCatalog.Get(tariff.Peak ? "composer.peak" : "composer.off_peak", Language);
        TariffText.Classes.Set("peak", tariff.Peak);
        TariffRemainingText.Text = remaining;
        TariffRemainingText.IsVisible = remaining.Length > 0;
        var key = tariff.Peak ? remaining.Length > 0 ? "composer.billed_at_the_vendors_published_4" : "composer.billed_at_the_vendors_published_3" : remaining.Length > 0 ? "composer.billed_at_the_vendors_published_2" : "composer.billed_at_the_vendors_published";
        ToolTip.SetTip(TariffMeter, MessageCatalog.Get(key, Language, ("left", remaining)));
        _tariffPulse.Start();
        var identity = (_engine?.Runtime?.Id ?? string.Empty) + ":" + tariff.ChangesAt;
        if (due && _tariffAsked != identity && _activePane is { } pane)
        {
            _tariffAsked = identity;
            _ = ReadPaneStatusAsync(pane);
        }
    }

    /// <summary>Formats the coarsest remaining billing interval without rounding a minute to zero.</summary>
    /// <param name="left">Time until the host's rate change.</param><returns>The localized countdown.</returns>
    private string TariffRemaining(TimeSpan left)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(left.TotalMinutes));
        if (minutes < 60)
        {
            return MessageCatalog.Get("common.n_min", Language, ("n", minutes.ToString(CultureInfo.CurrentCulture)));
        }

        var hours = minutes / 60;
        if (hours >= 24)
        {
            return MessageCatalog.Get("composer.n_d", Language, ("n", (hours / 24).ToString(CultureInfo.CurrentCulture)));
        }

        return minutes % 60 == 0 ? MessageCatalog.Get("common.n_h", Language, ("n", hours.ToString(CultureInfo.CurrentCulture))) : MessageCatalog.Get("composer.h_h_m_min", Language, ("h", hours.ToString(CultureInfo.CurrentCulture)), ("m", (minutes % 60).ToString(CultureInfo.CurrentCulture)));
    }
}
