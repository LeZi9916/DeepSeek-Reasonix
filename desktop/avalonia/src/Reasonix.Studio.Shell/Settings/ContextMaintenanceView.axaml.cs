using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>The live context gauge and the user-controlled compaction threshold.</summary>
internal sealed partial class ContextMaintenanceView : UserControl
{
    /// <summary>The smallest preview value on the reference slider.</summary>
    private const double MinimumRatio = 0.10;

    /// <summary>The largest preview value on the reference slider.</summary>
    private const double MaximumRatio = 0.99;

    /// <summary>The running session's acknowledged threshold.</summary>
    private JsonObject? _settings;

    /// <summary>Known token usage, if the context read was available.</summary>
    private double? _used;

    /// <summary>The committed or persisted ratio before the current gesture.</summary>
    private double _ratio;

    /// <summary>Whether the last settled preview value changed.</summary>
    private bool _moved;

    /// <summary>Suppresses edits while applying an authoritative result.</summary>
    private bool _drawing;

    /// <summary>Prevents duplicate writes when release and lost focus overlap.</summary>
    private bool _saving;

    /// <summary>The request owner's kernel.</summary>
    private StudioPageClient? _api;

    /// <summary>Display language for the group.</summary>
    private InterfaceLanguage _language;

    /// <summary>Cancellation token for the current runtime.</summary>
    private CancellationToken _token;

    /// <summary>Formats validation and runtime failures.</summary>
    private Func<Exception, string>? _describe;

    /// <summary>Initializes a new instance of the <see cref="ContextMaintenanceView"/> class.</summary>
    public ContextMaintenanceView()
    {
        InitializeComponent();
        ContextTrack.SizeChanged += (_, _) => DrawMeter();
        RatioSlider.SizeChanged += (_, _) => DrawPreview();
        RatioLayout.SizeChanged += (_, e) => RatioLayout.ColumnDefinitions[1].Width = new GridLength(Math.Max(90, (e.NewSize.Width - 80.8) / 2));
        RatioSlider.ValueChanged += (_, _) =>
        {
            _moved |= !_drawing;
            DrawPreview();
        };
        RatioSlider.AddHandler(InputElement.PointerReleasedEvent, async (_, _) => await CommitAsync().ConfigureAwait(true), RoutingStrategies.Bubble, handledEventsToo: true);
        RatioSlider.AddHandler(InputElement.KeyUpEvent, async (_, _) => await CommitAsync().ConfigureAwait(true), RoutingStrategies.Bubble, handledEventsToo: true);
        RatioSlider.LostFocus += async (_, _) => await CommitAsync().ConfigureAwait(true);
        RatioSlider.PointerCaptureLost += async (_, _) => await CommitAsync().ConfigureAwait(true);
    }

    /// <summary>Loads effective settings and the optional session gauge reading.</summary>
    /// <param name="api">The running session.</param>
    /// <param name="language">The settings language.</param>
    /// <param name="describe">Kernel message formatter.</param>
    /// <param name="token">Lifetime of the window.</param>
    /// <returns>The initial read.</returns>
    internal async Task OpenAsync(StudioPageClient api, InterfaceLanguage language, Func<Exception, string> describe, CancellationToken token)
    {
        _api = api;
        _language = language;
        _describe = describe;
        _token = token;
        UsedLabel.Text = Text("common.now");
        TriggerLabel.Text = Text("chat.next_maintenance");
        WindowLabel.Text = Text("chat.model_window");
        UnknownWindow.Text = Text("chat.this_source_declares_no_context");
        CapacityLabel.Text = Text("chat.capacity_guard");
        RatioLabel.Text = Text("chat.compact_ratio_control");
        RatioHint.Text = Text("chat.compact_ratio_control_hint");
        Unavailable.Text = Text("chat.the_compaction_settings_could_not");
        AutomationProperties.SetName(RatioSlider, RatioLabel.Text);

        var settings = api.CompactionSettingsAsync(token);
        var usage = ReadUsageAsync(api, token);
        _settings = await settings.ConfigureAwait(true);
        _used = await usage.ConfigureAwait(true);
        Unavailable.IsVisible = _settings is null;
        Maintenance.IsVisible = _settings is not null;
        if (_settings is not null)
        {
            _ratio = (double?)_settings["ratio"] ?? 0.9;
            SetPreview(_ratio);
            DrawGauge();
        }
    }

    /// <summary>Reads usage independently of the threshold setting.</summary>
    /// <param name="api">The running session.</param>
    /// <param name="token">Lifetime of the window.</param>
    /// <returns>The session token count when it was available.</returns>
    private static async Task<double?> ReadUsageAsync(StudioPageClient api, CancellationToken token)
    {
        try
        {
            return (double?)(await api.ContextAsync(token).ConfigureAwait(false))?["used"];
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Formats context counts with the same whole-thousand labels as the old page.</summary>
    /// <param name="count">Token count.</param>
    /// <returns>The display value.</returns>
    private static string Tokens(double count) => count >= 1000
        ? Math.Round(count / 1000, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + "k"
        : count.ToString(CultureInfo.InvariantCulture);

    /// <summary>Commits once when a moved control settles and redraws from the written state.</summary>
    /// <returns>The kernel write.</returns>
    private async Task CommitAsync()
    {
        if (_saving || !_moved || _settings is null || _api is not { } api)
        {
            return;
        }

        _moved = false;
        var next = Math.Round(RatioSlider.Value, 2);
        if (Math.Abs(next - _ratio) < 0.005)
        {
            return;
        }

        _saving = true;
        RatioSlider.IsEnabled = false;
        RatioFailure.IsVisible = false;
        try
        {
            _settings = await api.SetCompactRatioAsync(next, _token).ConfigureAwait(true);
            _ratio = (double?)_settings["ratio"] ?? next;
            SetPreview(_ratio);
            DrawGauge();
        }
        catch (Exception error)
        {
            if (!_token.IsCancellationRequested)
            {
                if (error is KernelRefusal { Code: "compaction.saved_while_running" or "runtime.rebuild_failed" })
                {
                    _ratio = next;
                    RatioFailure.Text = Text("common.saved_not_applied_yet") + " · " + _describe!(error);
                }
                else
                {
                    SetPreview(_ratio);
                    RatioFailure.Text = _describe!(error);
                }

                RatioFailure.IsVisible = true;
            }
        }
        finally
        {
            RatioSlider.IsEnabled = true;
            _saving = false;
        }
    }

    /// <summary>Updates the saved threshold and capacity without using the preview as a runtime measurement.</summary>
    private void DrawGauge()
    {
        var window = (double?)_settings?["context_window"] ?? 0;
        var trigger = (double?)_settings?["trigger"] ?? 0;
        var ratio = (double?)_settings?["ratio"] ?? _ratio;
        ContextGauge.IsVisible = window > 0;
        UnknownWindow.IsVisible = window <= 0;
        UsedValue.Text = _used is { } used ? Tokens(used) : "—";
        TriggerValue.Text = Tokens(trigger);
        WindowValue.Text = Tokens(window);
        ContextTrack.IsVisible = _used is not null;
        ContextNote.Text = MessageCatalog.Get("chat.this_session_should_tidy_its", _language, ("n", Tokens(trigger)));
        CapacityHint.Text = MessageCatalog.Get("chat.compacts_at_p_of_the", _language, ("p", Math.Round(ratio * 100).ToString(CultureInfo.InvariantCulture)));
        CapacityValue.Text = window <= 0 ? "—" : Tokens(Math.Round(window * ratio, MidpointRounding.AwayFromZero));
        DrawMeter();
    }

    /// <summary>Positions the context usage fill from the measured token count.</summary>
    private void DrawMeter()
    {
        var trigger = (double?)_settings?["trigger"] ?? 0;
        ContextFill.Width = trigger > 0 && _used is { } used ? Math.Clamp(used / trigger, 0, 1) * ContextTrack.Bounds.Width : 0;
    }

    /// <summary>Adopts a stored value without turning that adoption into a gesture.</summary>
    /// <param name="ratio">Stored value.</param>
    private void SetPreview(double ratio)
    {
        _drawing = true;
        RatioSlider.Value = Math.Clamp(ratio, MinimumRatio, MaximumRatio);
        _drawing = false;
        DrawPreview();
    }

    /// <summary>Draws the thumb percentage and track fill for the value under the pointer.</summary>
    private void DrawPreview()
    {
        var share = Math.Clamp((RatioSlider.Value - MinimumRatio) / (MaximumRatio - MinimumRatio), 0, 1);
        RatioFill.Width = share * RatioSlider.Bounds.Width;
        RatioValue.Text = Math.Round(RatioSlider.Value * 100, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>Reads a caption in the settings language.</summary>
    /// <param name="key">Message key.</param>
    /// <returns>The caption.</returns>
    private string Text(string key) => MessageCatalog.Get(key, _language);
}
