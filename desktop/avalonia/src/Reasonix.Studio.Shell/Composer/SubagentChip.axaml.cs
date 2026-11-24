using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Composer;

/// <summary>The retained subagent chip and its attached activity deck.</summary>
internal sealed partial class SubagentChip : UserControl
{
    /// <summary>Updates the watch times only while the deck is open.</summary>
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>The latest delegation readings.</summary>
    private SubagentActivity[] _entries = [];

    /// <summary>The snapshot that produced the current delegation readings.</summary>
    private SessionState? _state;

    /// <summary>The language of the mounted deck.</summary>
    private InterfaceLanguage? _language;

    /// <summary>Whether the clock has begun watching the current opening.</summary>
    private bool _watching;

    /// <summary>Initializes a new instance of the <see cref="SubagentChip"/> class.</summary>
    public SubagentChip()
    {
        InitializeComponent();
        Details.CanHover = () =>
        {
            Watch();
            return true;
        };
        Details.Pinned += Watch;
        DetachedFromVisualTree += (_, _) => Close();
        _clock.Tick += (_, _) =>
        {
            if (!Details.IsOpen)
            {
                _clock.Stop();
                _watching = false;
                return;
            }

            foreach (var row in Rows.Children.OfType<SubagentDeckRow>())
            {
                row.Tick();
            }
        };
        StopAll.Click += async (_, _) =>
        {
            if (StopRequested is not { } stop)
            {
                return;
            }

            StopAll.IsEnabled = false;
            try
            {
                await stop(_entries.Where(entry => entry.Running).Select(entry => entry.Call.Call.ID).Where(id => !string.IsNullOrEmpty(id)).ToArray()).ConfigureAwait(true);
            }
            finally
            {
                StopAll.IsEnabled = true;
            }
        };
    }

    /// <summary>Raised when the reader opens a delegate's record.</summary>
    internal event Action<SubagentActivity>? TranscriptRequested;

    /// <summary>Gets or sets the action that stops the selected running calls.</summary>
    internal Func<IReadOnlyList<string>, Task>? StopRequested
    {
        get; set;
    }

    /// <summary>Gets a value indicating whether any delegated agents remain active.</summary>
    internal bool HasRunning => _entries.Any(entry => entry.Running);

    /// <summary>Dismisses the deck on a session change.</summary>
    internal void Close()
    {
        _clock.Stop();
        _watching = false;
        Details.Close(immediately: true);
    }

    /// <summary>Updates the deck without remounting unchanged dispatches.</summary>
    /// <param name="state">The active session snapshot.</param>
    /// <param name="language">The reader's language.</param>
    internal void Show(SessionState state, InterfaceLanguage language)
    {
        if (_language == language && _state is { } prior && ReferenceEquals(prior.Items, state.Items) && ReferenceEquals(prior.SubagentPhases, state.SubagentPhases))
        {
            return;
        }

        _state = state;
        var entries = SubagentActivity.Of(state).ToArray();
        if (_language == language && _entries.SequenceEqual(entries))
        {
            return;
        }

        _language = language;
        _entries = entries;
        var running = entries.Where(entry => entry.Running).Sum(entry => entry.Count);
        var total = entries.Sum(entry => entry.Count);
        IsVisible = total > 0;
        Classes.Set("live", running > 0);
        Pulse.IsVisible = running > 0;
        if (total == 0)
        {
            Close();
        }

        AutomationProperties.SetName(Trigger, MessageCatalog.Get("common.subagents", language));
        Count.Text = Format.Count(running > 0 ? running : total, language);
        Caption.Text = MessageCatalog.Get(running > 0 ? "subagent.subagents_running" : "subagent.subagents_all_finished", language);
        Heading.Text = MessageCatalog.Get("subagent.deck.title", language);
        Summary.Text = MessageCatalog.Get("subagent.deck.count", language, ("live", Format.Count(running, language)), ("total", Format.Count(total, language)));
        Hint.Text = MessageCatalog.Get("subagent.deck.hint", language);
        StopAll.Content = MessageCatalog.Get("common.stop_all", language);
        StopAll.IsVisible = running > 0 && StopRequested is not null;
        var retained = Rows.Children.OfType<SubagentDeckRow>().ToDictionary(row => (string)row.Tag!, StringComparer.Ordinal);
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            if (!retained.TryGetValue(entry.Call.Id, out var row))
            {
                row = new SubagentDeckRow { Tag = entry.Call.Id };
                row.TranscriptRequested += item =>
                {
                    Close();
                    TranscriptRequested?.Invoke(item);
                };
                Rows.Children.Insert(index, row);
            }
            else
            {
                var old = Rows.Children.IndexOf(row);
                if (old != index)
                {
                    Rows.Children.Move(old, index);
                }
            }

            row.Show(entry, language);
        }

        while (Rows.Children.Count > entries.Length)
        {
            Rows.Children.RemoveAt(Rows.Children.Count - 1);
        }
    }

    /// <summary>Begins the elapsed-time reading when the user reveals the deck.</summary>
    private void Watch()
    {
        if (_watching && Details.IsOpen)
        {
            return;
        }

        _watching = true;
        foreach (var row in Rows.Children.OfType<SubagentDeckRow>())
        {
            row.BeginWatching();
        }

        _clock.Start();
    }
}
