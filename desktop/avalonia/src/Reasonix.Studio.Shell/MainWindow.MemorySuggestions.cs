using Reasonix.Studio.Kernel.Client;
using System;
using System.Linq;
using System.Text.Json.Nodes;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Identifies the runtime and turn boundary already inspected.</summary>
    private (StudioPageClient? Api, bool Running, string? Session) _suggestionsAt;

    /// <summary>Discards stale responses from a previous turn or session.</summary>
    private long _suggestionRead;

    /// <summary>Refreshes held proposals when loading an idle session or finishing a turn.</summary>
    /// <param name="running">Whether the turn is active.</param>
    private async void UpdateMemorySuggestions(bool running)
    {
        var api = _engine?.Pages;
        var key = (api, running, _sessionPath);
        if (_suggestionsAt == key)
        {
            return;
        }

        var ticket = ++_suggestionRead;
        if (!ReferenceEquals(api, _suggestionsAt.Api) || _sessionPath != _suggestionsAt.Session)
        {
            MemorySuggestions.Children.Clear();
            MemorySuggestions.IsVisible = false;
        }

        _suggestionsAt = key;
        if (running || api is null)
        {
            return;
        }

        try
        {
            var proposals = await api.MemorySuggestionsAsync(_closing.Token).ConfigureAwait(true);
            if (ticket != _suggestionRead || _closing.IsCancellationRequested)
            {
                return;
            }

            var ids = proposals.OfType<JsonObject>().Select(p => (string?)p["id"]).ToHashSet();
            foreach (var old in MemorySuggestions.Children.Where(c => !ids.Contains((string?)c.Tag)).ToArray())
            {
                MemorySuggestions.Children.Remove(old);
            }

            foreach (var proposal in proposals.OfType<JsonObject>())
            {
                if ((string?)proposal["id"] is not string id || MemorySuggestions.Children.Any(c => Equals(c.Tag, id)))
                {
                    continue;
                }

                var card = new Settings.MemorySuggestionEntry();
                card.Open(proposal, Language, api, RemoveCard, ShowRefusal, _closing.Token);
                MemorySuggestions.Children.Add(card);

                void RemoveCard()
                {
                    if (MemorySuggestions.Children.Remove(card))
                    {
                        _suggestionRead++;
                        MemorySuggestions.IsVisible = MemorySuggestions.Children.Count > 0;
                    }
                }
            }

            MemorySuggestions.IsVisible = MemorySuggestions.Children.Count > 0;
        }
        catch (Exception error)
        {
            if (!_closing.IsCancellationRequested && ticket == _suggestionRead)
            {
                ShowRefusal(error);
            }
        }
    }
}
