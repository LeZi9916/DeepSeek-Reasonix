using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Reasonix.Studio.Pages.I18n;
using System;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The platform-local timer for the next scheduled theme change.</summary>
    private readonly DispatcherTimer _themeScheduleTimer = new();

    /// <summary>Gets the saved theme choice independently of the applied color scheme.</summary>
    internal string ThemeMode { get; private set; } = "Default";

    /// <summary>Identifies the dark interval with an inclusive opening and exclusive closing time.</summary>
    /// <param name="start">Local time when dark mode starts.</param><param name="end">Local time when light mode resumes.</param>
    /// <param name="time">Effective local wall time.</param><returns>Whether dark mode is due.</returns>
    internal static bool IsScheduledDark(TimeOnly start, TimeOnly end, TimeOnly time) =>
        start < end ? time >= start && time < end : start > end && (time >= start || time < end);

    /// <summary>Connects scheduled effects to the desktop lifecycle.</summary>
    private void InitializeThemeSchedule()
    {
        _themeScheduleTimer.Tick += (_, _) => ApplyScheduledTheme();
        Activated += (_, _) => ApplyScheduledTheme();
        Closed += (_, _) => _themeScheduleTimer.Stop();
    }

    /// <summary>Adopts an explicit or scheduled appearance.</summary>
    /// <param name="mode">Device-local appearance mode.</param>
    private void SetThemeMode(string? mode)
    {
        ThemeMode = mode is "Dark" or "Light" or "Scheduled" ? mode : "Default";
        _themeScheduleTimer.Stop();
        if (ThemeMode == "Scheduled")
        {
            ApplyScheduledTheme();
        }
        else
        {
            RequestedThemeVariant = ThemeMode == "Dark" ? ThemeVariant.Dark : ThemeMode == "Light" ? ThemeVariant.Light : ThemeVariant.Default;
        }
    }

    /// <summary>Updates the active scheme and arms the next local-time boundary.</summary>
    private void ApplyScheduledTheme()
    {
        _themeScheduleTimer.Stop();
        if (ThemeMode != "Scheduled")
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var start = _workspacePreferences.DarkThemeStart;
        var end = _workspacePreferences.DarkThemeEnd;
        var dark = IsScheduledDark(start, end, TimeOnly.FromDateTime(now.LocalDateTime));
        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        if (start == end)
        {
            return;
        }

        var candidate = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMinute));
        for (var minute = 0; minute < 26 * 60; minute++)
        {
            candidate = candidate.AddMinutes(1);
            if (IsScheduledDark(start, end, TimeOnly.FromDateTime(candidate.LocalDateTime)) != dark)
            {
                _themeScheduleTimer.Interval = candidate - now;
                _themeScheduleTimer.Start();
                break;
            }
        }
    }

    /// <summary>Creates local time inputs for the scheduled theme.</summary>
    /// <returns>The fields shown when the scheduled mode is selected.</returns>
    private StackPanel ThemeScheduleFields()
    {
        var fields = new StackPanel { Spacing = 5, IsVisible = ThemeMode == "Scheduled" };
        fields.Children.Add(Detail(MessageCatalog.Get("look.switches_daily_in_local_time", Language)));
        var equal = Detail(MessageCatalog.Get("look.equal_start_and_end_times", Language));
        equal.IsVisible = _workspacePreferences.DarkThemeStart == _workspacePreferences.DarkThemeEnd;
        AddTime("look.dark_mode_starts", _workspacePreferences.DarkThemeStart, "start");
        AddTime("look.light_mode_resumes", _workspacePreferences.DarkThemeEnd, "end");
        fields.Children.Add(equal);
        return fields;

        void AddTime(string label, TimeOnly initial, string field)
        {
            var picker = new TimePicker { ClockIdentifier = "24HourClock", SelectedTime = initial.ToTimeSpan() };
            AutomationProperties.SetAutomationId(picker, "action:appearance.schedule." + field);
            AddRow(fields, label, picker);
            picker.SelectedTimeChanged += (_, _) =>
            {
                if (picker.SelectedTime is not TimeSpan time)
                {
                    return;
                }

                var selected = TimeOnly.FromTimeSpan(time);
                _workspacePreferences = field == "start"
                    ? _workspacePreferences with
                    {
                        DarkThemeStart = selected,
                    }
                    : _workspacePreferences with
                    {
                        DarkThemeEnd = selected,
                    };
                if (_connectOnOpen)
                {
                    _workspacePreferences.Save();
                }

                equal.IsVisible = _workspacePreferences.DarkThemeStart == _workspacePreferences.DarkThemeEnd;
                ApplyScheduledTheme();
            };
        }
    }
}
