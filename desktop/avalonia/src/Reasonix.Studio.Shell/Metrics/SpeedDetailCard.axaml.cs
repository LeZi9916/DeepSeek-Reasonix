using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State;
using System.Globalization;

namespace Reasonix.Studio.Shell.Metrics;

/// <summary>The generation figures supported by live arrivals and measured model rounds.</summary>
public sealed partial class SpeedDetailCard : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="SpeedDetailCard"/> class.</summary>
    public SpeedDetailCard() => InitializeComponent();

    /// <summary>Projects generation measurements without substituting zero for unknown timings.</summary>
    /// <param name="reading">Supported speed values.</param><param name="running">Canonical turn state.</param>
    /// <param name="language">Interface language.</param>
    public void Show(GenerationReading reading, bool running, InterfaceLanguage language)
    {
        string Say(string key) => MessageCatalog.Get(key, language);
        Title.Text = Say("composer.generation_speed");
        Scope.Text = Say(running ? "composer.live" : "composer.latest_run");
        CurrentLabel.Text = Say("composer.current_rate");
        AverageLabel.Text = Say("composer.run_average");
        FirstLabel.Text = Say("composer.time_to_first_token");
        OutputLabel.Text = Say("composer.run_output");
        TimeLabel.Text = Say("composer.model_time");
        Current.Text = reading.CurrentRate > 0 ? reading.CurrentRate.ToString("0.0", CultureInfo.CurrentCulture) + " tok/s" : "—";
        Average.Text = reading.AverageRate > 0 ? reading.AverageRate.ToString("0.0", CultureInfo.CurrentCulture) + " tok/s" : "—";
        First.Text = reading.FirstTokenSeconds > 0 ? reading.FirstTokenSeconds.ToString("0.00", CultureInfo.CurrentCulture) + "s" : "—";
        Output.Text = reading.OutputTokens > 0 ? reading.OutputTokens.ToString("N0", CultureInfo.CurrentCulture) + " tokens" : "—";
        Time.Text = reading.ModelSeconds > 0 ? reading.ModelSeconds.ToString("0.0", CultureInfo.CurrentCulture) + "s" : "—";
        Explanation.Text = Say("composer.current_rate_estimates_the_latest");
    }
}
