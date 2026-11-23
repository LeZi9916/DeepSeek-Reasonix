using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>The current account's new submission or next-version draft.</summary>
internal sealed partial class MarketPublishView : SettingsFormView
{
    /// <summary>Supported package categories and their localized names.</summary>
    private static readonly (string Value, string Label)[] s_kinds =
    [
        ("skill", "settings.skills"),
        ("plugin", "ext.plugins"),
        ("mcp", "persona.mcp_servers"),
        ("theme", "ext.theme"),
    ];

    /// <summary>Editable copies of the tags inherited from an earlier version.</summary>
    private readonly List<(StackPanel Field, TextBox Input)> _existingTags = [];

    /// <summary>Current package category.</summary>
    private string _kind = "skill";

    /// <summary>Whether the current form has a submission in flight.</summary>
    private bool _saving;

    /// <summary>The account under which the submission is sent.</summary>
    private string _handle = string.Empty;

    /// <summary>Initializes a new instance of the <see cref="MarketPublishView"/> class.</summary>
    public MarketPublishView()
    {
        InitializeComponent();
        PublishFrame.SizeChanged += (_, e) =>
        {
            FooterGrid.ColumnDefinitions = new ColumnDefinitions(e.NewSize.Width <= 560 ? "*,Auto" : "*,Auto,*");
            FooterGrid.ColumnDefinitions[0].MaxWidth = 490.8;
        };
    }

    /// <summary>Shows a publisher form tied to the current account.</summary>
    /// <param name="api">The current runtime.</param>
    /// <param name="language">Settings language.</param>
    /// <param name="handle">Current publisher handle.</param>
    /// <param name="earlier">Previous package metadata, when publishing a new version.</param>
    /// <param name="describe">Formats refusals.</param>
    /// <param name="showMine">Returns to the publisher inventory.</param>
    /// <param name="setApplying">Locks ownership-changing navigation during submission.</param>
    /// <param name="token">Window lifetime.</param>
    internal void Open(
        StudioPageClient api,
        InterfaceLanguage language,
        string handle,
        JsonObject? earlier,
        Func<Exception, string> describe,
        Func<Task> showMine,
        Action<bool> setApplying,
        CancellationToken token)
    {
        _handle = handle;
        Translate(language);
        AutomationProperties.SetName(NewTags, Text("ext.tags"));
        foreach (var (value, label) in s_kinds)
        {
            var choice = new Button { Tag = value, Content = Text(label), Classes = { "market-option" } };
            AutomationProperties.SetAutomationId(choice, "action:market.draft.kind");
            choice.Click += (_, _) =>
            {
                _kind = value;
                Draw();
            };
            choice.KeyDown += (_, args) =>
            {
                if (args.Key is not (Key.Left or Key.Right or Key.Up or Key.Down))
                {
                    return;
                }

                var current = Array.FindIndex(s_kinds, item => item.Value == _kind);
                var backward = args.Key is Key.Left or Key.Up;
                _kind = s_kinds[(current + (backward ? s_kinds.Length - 1 : 1)) % s_kinds.Length].Value;
                Draw();
                Kinds.Children.OfType<Button>().First(item => Equals(item.Tag, _kind)).Focus();
                args.Handled = true;
            };
            Kinds.Children.Add(choice);
        }

        Visibility.IsCheckedChanged += (_, _) => Draw();
        Source.TextChanged += (_, _) => Draw();
        PackageName.TextChanged += (_, _) => Draw();
        Reset(earlier);
        if (earlier is not null)
        {
            Dispatcher.UIThread.Post(() => Source.Focus());
        }

        Submit.Click += async (_, _) => await SendAsync().ConfigureAwait(true);
        Source.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter && Submit.IsEnabled)
            {
                args.Handled = true;
                await SendAsync().ConfigureAwait(true);
            }
        };
        PackageName.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter && Submit.IsEnabled)
            {
                args.Handled = true;
                await SendAsync().ConfigureAwait(true);
            }
        };
        PublishAgain.Click += (_, _) => Reset(null);
        SeeMine.Click += async (_, _) => await showMine().ConfigureAwait(true);

        async Task SendAsync()
        {
            if (_saving || !Submit.IsEnabled || !Draft.IsVisible)
            {
                return;
            }

            _saving = true;
            setApplying(true);
            ErrorPanel.IsVisible = false;
            Draw();
            try
            {
                var tags = _existingTags.Select(field => field.Input.Text ?? string.Empty)
                    .Concat((NewTags.Text ?? string.Empty).Split(',', '，').Select(tag => tag.Trim())).Where(tag => tag.Length > 0);
                var isPrivate = Visibility.IsChecked == true;
                var request = new JsonObject
                {
                    ["kind"] = _kind,
                    ["name"] = PackageName.Text ?? string.Empty,
                    ["source"] = Source.Text ?? string.Empty,
                    ["summary"] = Summary.Text ?? string.Empty,
                    ["description"] = Description.Text ?? string.Empty,
                    ["repoUrl"] = Repository.Text ?? string.Empty,
                    ["version"] = Version.Text ?? string.Empty,
                    ["tags"] = new JsonArray(tags.Select(tag => (JsonNode?)JsonValue.Create(tag)).ToArray()),
                    ["visibility"] = isPrivate ? "private" : "public",
                };
                var receipt = await api.PublishMarketAsync(request, token).ConfigureAwait(true);
                var receiptSlug = (string?)receipt["package"]?["slug"] ?? string.Empty;
                var receiptVersion = (string?)receipt["version"] ?? string.Empty;
                var keptPrivate = (string?)receipt["package"]?["status"] == "private";
                ReceiptTitle.Text = Text(
                    keptPrivate ? "ext.saved_slug_version_visible_only" : "ext.submitted_slug_version",
                    ("slug", receiptSlug),
                    ("version", receiptVersion));
                ReceiptHint.Text = Text(keptPrivate ? "ext.it_skips_review_and_never" : "ext.it_goes_public_in_the");
                Receipt.IsVisible = true;
                Draft.IsVisible = false;
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    Failure.Text = describe(error);
                    ErrorPanel.IsVisible = true;
                }
            }
            finally
            {
                _saving = false;
                setApplying(false);
                Draw();
            }
        }
    }

    /// <summary>Applies the empty form or previous-version values to the draft.</summary>
    /// <param name="earlier">The earlier package whose metadata can be reused.</param>
    private void Reset(JsonObject? earlier)
    {
        _kind = (string?)earlier?["kind"] ?? "skill";
        Source.Text = Version.Text = NewTags.Text = string.Empty;
        PackageName.Text = (string?)earlier?["name"] ?? string.Empty;
        Summary.Text = (string?)earlier?["summary"] ?? string.Empty;
        Description.Text = (string?)earlier?["description"] ?? string.Empty;
        Repository.Text = (string?)earlier?["repoUrl"] ?? string.Empty;
        Visibility.IsChecked = (string?)earlier?["status"] == "private";
        Origin.Text = Text("ext.reuse_details_from_slug_enter", ("slug", (string?)earlier?["slug"] ?? string.Empty));
        Origin.IsVisible = ExistingNameHint.IsVisible = earlier is not null;
        var inherited = earlier?["tags"]?.AsArray().Select(item => (string?)item).OfType<string>().ToArray() ?? [];
        SetTags(inherited, earlier is not null);
        Receipt.IsVisible = ErrorPanel.IsVisible = false;
        Draft.IsVisible = true;
        Draw();
    }

    /// <summary>Rebuilds the editable tag cells while retaining the grid's order.</summary>
    /// <param name="tags">Tags on the earlier package.</param>
    /// <param name="fromEarlier">Whether this is a new version.</param>
    private void SetTags(string[] tags, bool fromEarlier)
    {
        foreach (var (field, _) in _existingTags)
        {
            TagFields.Children.Remove(field);
        }

        _existingTags.Clear();
        TagFields.RowDefinitions.Clear();
        for (var i = 0; i < (tags.Length + 3) / 2; i++)
        {
            TagFields.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        for (var i = 0; i < tags.Length; i++)
        {
            var label = Text("ext.existing_tag_n", ("n", (i + 1).ToString(CultureInfo.CurrentCulture)));
            var input = new TextBox { Text = tags[i], Classes = { "publish-field" } };
            AutomationProperties.SetAutomationId(input, "action:market.draft.tags");
            AutomationProperties.SetName(input, label);
            var cell = new StackPanel
            {
                Spacing = 4,
                Children = { new TextBlock { Text = label, Classes = { "publish-label" } }, input },
            };
            var index = i + 1;
            Grid.SetColumn(cell, index % 2);
            Grid.SetRow(cell, index / 2);
            _existingTags.Add((cell, input));
            TagFields.Children.Insert(TagFields.Children.IndexOf(NewTagField), cell);
        }

        var next = tags.Length + 1;
        Grid.SetColumn(NewTagField, next % 2);
        Grid.SetRow(NewTagField, next / 2);
        NewTagLabel.Text = Text(fromEarlier ? "ext.add_tags" : "ext.tags");
        AutomationProperties.SetName(NewTags, NewTagLabel.Text);
    }

    /// <summary>Applies live validation and the current source and visibility labels.</summary>
    private void Draw()
    {
        foreach (var button in Kinds.Children.OfType<Button>())
        {
            button.Classes.Set("active", Equals(button.Tag, _kind));
        }

        var isPrivate = Visibility.IsChecked == true;
        Identity.Text = Text(isPrivate ? "ext.saved_as_handle_visible_only" : "ext.submitted_as_handle_and_made", ("handle", _handle));
        ThemeHint.IsVisible = _kind == "theme";
        SourceHint.Text = Text(_kind switch
        {
            "plugin" or "theme" => "ext.a_github_directory_pinned_to",
            "mcp" => "ext.an_https_address_of_a",
            _ => "ext.an_https_address_of_a_2",
        });
        FooterNote.Text = Text(isPrivate ? "ext.only_you_will_see_it" : "ext.it_joins_the_review_queue");
        Submit.Content = Text(_saving ? "ext.submitting" : isPrivate ? "ext.save_as_private" : "ext.submit_for_review");
        var enabled = !_saving;
        Kinds.IsEnabled = Visibility.IsEnabled = Source.IsEnabled = PackageName.IsEnabled = Version.IsEnabled
            = Summary.IsEnabled = Description.IsEnabled = TagFields.IsEnabled = enabled;
        Submit.IsEnabled = enabled && !string.IsNullOrWhiteSpace(PackageName.Text) && !string.IsNullOrWhiteSpace(Source.Text);
    }
}
