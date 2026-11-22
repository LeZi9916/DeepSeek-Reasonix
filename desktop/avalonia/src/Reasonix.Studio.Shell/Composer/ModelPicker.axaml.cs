using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Path = Avalonia.Controls.Shapes.Path;

namespace Reasonix.Studio.Shell.Composer;

/// <summary>The composer's model and reasoning-effort chooser.</summary>
public sealed partial class ModelPicker : UserControl
{
    /// <summary>Defines the trigger the popup is anchored to.</summary>
    public static readonly StyledProperty<Control?> AnchorProperty = AvaloniaProperty.Register<ModelPicker, Control?>(nameof(Anchor));

    /// <summary>Rows past this count are reachable by searching.</summary>
    private const int RowCap = 60;

    /// <summary>Translations for effort levels, in the catalog's vocabulary.</summary>
    private static readonly IReadOnlyDictionary<string, string> s_effortLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["auto"] = "common.auto",
        ["disabled"] = "common.close",
        ["none"] = "composer.no_reasoning",
        ["minimal"] = "composer.minimal",
        ["low"] = "composer.fast",
        ["medium"] = "composer.balanced",
        ["high"] = "composer.deep",
        ["xhigh"] = "composer.extra_deep",
        ["max"] = "composer.maximum",
    };

    /// <summary>Account colours, stable across a launch for names no preset declares.</summary>
    private static readonly Color[] s_tones =
    [
        Color.Parse("#4D6BFE"),
        Color.Parse("#C46A4A"),
        Color.Parse("#5E8C61"),
        Color.Parse("#9A6FB0"),
        Color.Parse("#C49A3A"),
    ];

    /// <summary>Provider keys the reader narrowed the list to.</summary>
    private readonly HashSet<string> _chosen = new(StringComparer.Ordinal);

    /// <summary>Models as the kernel listed them for this opening.</summary>
    private IReadOnlyList<ModelChoice> _models = [];

    /// <summary>Accounts in the order the kernel listed them.</summary>
    private IReadOnlyList<Account> _accounts = [];

    /// <summary>The search terms currently narrowing rows.</summary>
    private string _query = string.Empty;

    /// <summary>The model row Enter would take, when a query narrowed the list.</summary>
    private string? _firstHit;

    /// <summary>What the kernel last said about the open session.</summary>
    private SessionStatus _status = new();

    /// <summary>The language this opening draws its labels in.</summary>
    private InterfaceLanguage _language = InterfaceLanguage.Zh;

    /// <summary>Initializes a new instance of the <see cref="ModelPicker"/> class.</summary>
    public ModelPicker()
    {
        InitializeComponent();
        Search.TextChanged += (_, _) =>
        {
            _query = Search.Text ?? string.Empty;
            DrawModels();
        };
        Search.KeyDown += OnSearchKey;
        Manage.Click += (_, _) =>
        {
            Close();
            ManageRequested?.Invoke();
        };
        Surface.Closed += (_, _) =>
        {
            Anchor?.Classes.Remove("open");
            Search.Text = string.Empty;
            Closed?.Invoke();
        };
        Surface.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            Surface.VerticalOffset = -13;
            Search.Focus();
            Search.SelectAll();
        });
    }

    /// <summary>Raised when the reader chose another model reference.</summary>
    public event Action<string>? ModelPicked;

    /// <summary>Raised when the reader chose a reasoning level.</summary>
    public event Action<string>? EffortPicked;

    /// <summary>Raised when the reader toggled a model mode.</summary>
    public event Action<string>? ModePicked;

    /// <summary>Raised when the reader asked for the model and connection settings.</summary>
    public event Action? ManageRequested;

    /// <summary>Raised after the popup closed, including light dismissal.</summary>
    public event Action? Closed;

    /// <summary>Gets or sets the control the popup is anchored to.</summary>
    public Control? Anchor
    {
        get => GetValue(AnchorProperty);
        set => SetValue(AnchorProperty, value);
    }

    /// <summary>Gets a value indicating whether the popup is visible.</summary>
    public bool IsOpen => Surface.IsOpen;

    /// <summary>Gives an effort its human label.</summary>
    /// <param name="level">Kernel effort value.</param>
    /// <returns>The localized label, or the value itself.</returns>
    public string EffortText(string level) =>
        s_effortLabels.TryGetValue(level, out var key) ? MessageCatalog.Get(key, _language) : level;

    /// <summary>Shows the picker with the kernel's current catalog and status.</summary>
    /// <param name="models">Chat models the kernel listed.</param>
    /// <param name="status">The session status the composer last read.</param>
    /// <param name="language">Language for the panel's labels.</param>
    /// <param name="focusEffort">Whether the effort column should take focus first.</param>
    public void Show(IReadOnlyList<ModelChoice> models, SessionStatus status, InterfaceLanguage language, bool focusEffort = false)
    {
        Bind(models, status, language);
        Open(focusEffort);
    }

    /// <summary>Rebuilds an already open picker after a model write read back.</summary>
    /// <param name="models">Chat models the kernel listed.</param>
    /// <param name="status">The settled session status.</param>
    /// <param name="language">Language for the labels.</param>
    public void Refresh(IReadOnlyList<ModelChoice> models, SessionStatus status, InterfaceLanguage language)
    {
        if (!IsOpen)
        {
            return;
        }

        Bind(models, status, language);
    }

    /// <summary>Closes the popup without changing the session.</summary>
    public void Close() => Surface.IsOpen = false;

    /// <summary>Takes a catalog and session status into the panel's fields.</summary>
    /// <param name="models">Chat models the kernel listed.</param>
    /// <param name="status">The session status.</param>
    /// <param name="language">Language for the labels.</param>
    private void Bind(IReadOnlyList<ModelChoice> models, SessionStatus status, InterfaceLanguage language)
    {
        _models = models;
        _status = status;
        _language = language;
        _accounts = Group(models, status.ModelRef);
        _chosen.IntersectWith(_accounts.Select(account => account.Key));
        BuildChrome();
        DrawChips();
        DrawModels();
        DrawEfforts();
    }

    /// <summary>Positions the surface inside the window and opens it.</summary>
    /// <param name="focusEffort">Whether the effort column should take focus first.</param>
    private void Open(bool focusEffort)
    {
        if (Anchor is null)
        {
            return;
        }

        Surface.PlacementTarget = Anchor;
        Surface.VerticalOffset = -13;
        var top = TopLevel.GetTopLevel(this);
        var viewport = top?.ClientSize ?? new Size(900, 700);
        var available = Math.Max(320, viewport.Width - 24);
        var width = Math.Min(700, available);
        var narrow = width < 640;
        Frame.Width = width;
        Frame.Classes.Set("narrow", narrow);
        Layout.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(340);
        Layout.RowDefinitions[5].Height = narrow ? new GridLength(0, GridUnitType.Auto) : new GridLength(0);
        Grid.SetRow(Aside, narrow ? 5 : 0);
        Grid.SetRowSpan(Aside, narrow ? 1 : 5);
        Grid.SetColumn(Aside, narrow ? 0 : 1);
        Aside.BorderThickness = narrow ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0);
        ModelScroll.MaxHeight = Math.Clamp(viewport.Height - 250, 160, narrow ? 280 : 440);
        EffortScroll.MaxHeight = Math.Clamp(viewport.Height - 160, 180, 520);
        Frame.BoxShadow = BoxShadows.Parse(ActualThemeVariant == ThemeVariant.Dark
            ? "0 0 0 1 #59000000, 0 22 60 -22 #D9000000"
            : "inset 0 1 0 #E6FFFFFF, 0 18 44 -18 #33171E2D");
        Anchor.Classes.Add("open");
        Surface.IsOpen = true;
        if (focusEffort)
        {
            Dispatcher.UIThread.Post(() => EffortList.Children.OfType<Button>().FirstOrDefault()?.Focus());
        }
    }

    /// <summary>Draws the fixed labels and placeholders in the requested language.</summary>
    private void BuildChrome()
    {
        PickTitle.Text = MessageCatalog.Get("composer.choose_model", _language);
        PickSubtitle.Text = MessageCatalog.Get("composer.for_future_tasks", _language);
        Search.PlaceholderText = MessageCatalog.Get("composer.search_models_or_providers", _language);
        ManageLabel.Text = MessageCatalog.Get("composer.manage_models_and_connections", _language);
        EffortTitle.Text = MessageCatalog.Get("composer.reasoning_effort", _language);
        AutomationProperties.SetName(Search, MessageCatalog.Get("composer.search_models_or_providers", _language));
        AutomationProperties.SetName(Manage, MessageCatalog.Get("composer.manage_models_and_connections", _language));
        AutomationProperties.SetName(Chips, MessageCatalog.Get("models.filter_by_provider", _language));
    }

    /// <summary>Draws one chip per account, plus the clear chip when a filter is on.</summary>
    private void DrawChips()
    {
        Chips.Children.Clear();
        if (_accounts.Count < 2)
        {
            Chips.IsVisible = false;
            return;
        }

        Chips.IsVisible = true;
        foreach (var account in _accounts)
        {
            var chip = Chip(account.Label, _chosen.Contains(account.Key));
            chip.Click += (_, _) =>
            {
                if (!_chosen.Remove(account.Key))
                {
                    _chosen.Add(account.Key);
                }

                DrawChips();
                DrawModels();
            };
            Chips.Children.Add(chip);
        }

        if (_chosen.Count > 0)
        {
            var clear = Chip(MessageCatalog.Get("common.clear_2", _language), false);
            clear.Classes.Add("clear");
            clear.Click += (_, _) =>
            {
                _chosen.Clear();
                DrawChips();
                DrawModels();
            };
            Chips.Children.Add(clear);
        }
    }

    /// <summary>Builds one provider chip.</summary>
    /// <param name="label">Visible provider label.</param>
    /// <param name="on">Whether the provider is in the current filter.</param>
    /// <returns>The chip.</returns>
    private Button Chip(string label, bool on)
    {
        var chip = new Button { Content = label };
        chip.Classes.Add("model-chip");
        chip.Classes.Set("on", on);
        return chip;
    }

    /// <summary>Draws the pinned current row, account headings and capped model rows.</summary>
    private void DrawModels()
    {
        ModelList.Children.Clear();
        _firstHit = null;
        var terms = Normalize(_query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var active = ActiveModel();
        var current = active?.Ref ?? _status.ModelRef;
        var rows = 0;
        var hidden = 0;
        var added = false;
        if (active is not null && Matches(active, terms))
        {
            ModelList.Children.Add(ModelRow(active, active.Ref == current));
            added = true;
            rows++;
            if (terms.Length > 0)
            {
                _firstHit = active.Ref;
            }
        }

        foreach (var account in _accounts)
        {
            if (_chosen.Count > 0 && !_chosen.Contains(account.Key))
            {
                continue;
            }

            var matches = account.Models.Where(model => !ReferenceEquals(model, active) && Matches(model, terms)).ToList();
            if (matches.Count == 0)
            {
                continue;
            }

            var room = Math.Max(RowCap - rows, 0);
            var shown = matches.Take(room).ToList();
            hidden += matches.Count - shown.Count;
            if (shown.Count == 0)
            {
                continue;
            }

            ModelList.Children.Add(GroupHead(account, added));
            added = true;
            foreach (var model in shown)
            {
                ModelList.Children.Add(ModelRow(model, model.Ref == current));
                rows++;
                _firstHit ??= terms.Length > 0 ? model.Ref : null;
            }
        }

        if (rows == 0)
        {
            ModelList.Children.Add(new TextBlock
            {
                Text = MessageCatalog.Get("composer.no_models_match", _language),
                Classes = { "model-empty" },
            });
        }

        if (hidden > 0)
        {
            ModelList.Children.Add(new TextBlock
            {
                Text = MessageCatalog.Get("composer.n_more_not_listed_search", _language, ("n", hidden.ToString(System.Globalization.CultureInfo.CurrentCulture))),
                Classes = { "model-more" },
            });
        }

        SearchCount.Text = terms.Length == 0 ? string.Empty : $"{rows} / {_models.Count}";
    }

    /// <summary>Draws the effort ladder, model modes and their footer note.</summary>
    private void DrawEfforts()
    {
        EffortList.Children.Clear();
        var active = ActiveModel();
        EffortModel.Text = active is null ? ShortRef(_status.ModelRef) : Label(active);
        var levels = active?.Efforts ?? [];
        var current = _status.Effort;
        if (levels.Length == 0)
        {
            EffortList.Children.Add(DeclareRow());
            EffortNote.Text = MessageCatalog.Get("composer.this_endpoint_reported_no_reasoning", _language);
        }
        else
        {
            var cheapest = levels.FirstOrDefault(level => !level.Equals("auto", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
            foreach (var level in levels)
            {
                EffortList.Children.Add(EffortRow(level, current, active?.ForcesThinking == true, cheapest));
            }

            EffortNote.Text = MessageCatalog.Get("composer.only_levels_actually_supported_by", _language);
        }

        var modes = _status.Modes;
        for (var i = 0; i < modes.Length; i++)
        {
            EffortList.Children.Add(ModeRow(modes[i], i == 0));
        }
    }

    /// <summary>Closes and reports a model choice.</summary>
    /// <param name="reference">Model reference.</param>
    private void Choose(string reference)
    {
        ModelPicked?.Invoke(reference);
    }

    /// <summary>Closes and reports an effort choice.</summary>
    /// <param name="level">Kernel effort value.</param>
    private void ChooseEffort(string level)
    {
        Close();
        EffortPicked?.Invoke(level);
    }

    /// <summary>Handles search keys without leaking them to the composer.</summary>
    /// <param name="sender">The search field.</param>
    /// <param name="args">Key press.</param>
    private void OnSearchKey(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape)
        {
            Close();
            args.Handled = true;
        }
        else if (args.Key == Key.Enter && _firstHit is { Length: > 0 } reference)
        {
            Choose(reference);
            args.Handled = true;
        }
        else if (args.Key == Key.Down)
        {
            ModelList.Children.OfType<Button>().FirstOrDefault()?.Focus();
            args.Handled = true;
        }
    }

    /// <summary>Groups the catalog by endpoint and credential slot.</summary>
    /// <param name="models">Kernel catalog.</param>
    /// <param name="current">The session's model reference.</param>
    /// <returns>Accounts in listing order.</returns>
    private IReadOnlyList<Account> Group(IReadOnlyList<ModelChoice> models, string current)
    {
        var order = new List<string>();
        var byKey = new Dictionary<string, List<ModelChoice>>(StringComparer.Ordinal);
        foreach (var model in models)
        {
            var key = AccountKey(model);
            if (!byKey.TryGetValue(key, out var list))
            {
                list = [];
                byKey[key] = list;
                order.Add(key);
            }

            list.Add(model);
        }

        return order.Select(key =>
        {
            var list = byKey[key];
            var host = HostOf(list[0]);
            return new Account(key, AccountLabel(list, host), host, MarkOf(list[0]), list);
        }).ToList();
    }

    /// <summary>Names the account a model route belongs to.</summary>
    /// <param name="models">Models on the account.</param>
    /// <param name="host">Endpoint host.</param>
    /// <returns>The label a person would use.</returns>
    private string AccountLabel(IReadOnlyList<ModelChoice> models, string host)
    {
        var display = models.Select(model => model.DisplayName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
        if (!string.IsNullOrWhiteSpace(display))
        {
            return display.Trim();
        }

        var provider = models[0].Provider;
        var vendor = VendorLabel(host);
        if (models.Any(model => !model.Preset && !model.Provider.Equals(vendor, StringComparison.OrdinalIgnoreCase)) || vendor.Length == 0)
        {
            return provider;
        }

        return vendor;
    }

    /// <summary>Uses the endpoint host, else the provider name when nothing declared one.</summary>
    /// <param name="model">A model on the account.</param>
    /// <returns>The account host.</returns>
    private string HostOf(ModelChoice model) =>
        !string.IsNullOrWhiteSpace(model.Vendor) ? model.Vendor : model.Provider;

    /// <summary>Derives the short account name from a host.</summary>
    /// <param name="host">Endpoint host or provider name.</param>
    /// <returns>The label a person would use for the service.</returns>
    private string VendorLabel(string host)
    {
        var bare = host.Trim().ToLowerInvariant();
        var scheme = bare.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            bare = bare[(scheme + 3)..];
        }

        var slash = bare.IndexOf('/');
        if (slash >= 0)
        {
            bare = bare[..slash];
        }

        var colon = bare.IndexOf(':');
        if (colon >= 0)
        {
            bare = bare[..colon];
        }

        foreach (var prefix in new[] { "www.", "api.", "open.", "gateway." })
        {
            if (bare.StartsWith(prefix, StringComparison.Ordinal))
            {
                bare = bare[prefix.Length..];
                break;
            }
        }

        var dot = bare.IndexOf('.');
        return dot > 0 ? bare[..dot] : bare;
    }

    /// <summary>Names the account key that keeps two credentials on one host apart.</summary>
    /// <param name="model">A model on the account.</param>
    /// <returns>The account key.</returns>
    private string AccountKey(ModelChoice model) => HostOf(model) + "\0" + (model.KeyEnv ?? string.Empty).Trim();

    /// <summary>Finds the model the session is on.</summary>
    /// <returns>The active model, or null before the catalog carries it.</returns>
    private ModelChoice? ActiveModel() =>
        _models.FirstOrDefault(model => model.Active) ?? _models.FirstOrDefault(model => model.Ref == _status.ModelRef);

    /// <summary>Whether a model matches every search term and its account's name.</summary>
    /// <param name="model">Candidate row.</param>
    /// <param name="terms">Normalized terms.</param>
    /// <returns>Whether the row stays visible.</returns>
    private bool Matches(ModelChoice model, string[] terms)
    {
        if (terms.Length == 0)
        {
            return true;
        }

        var hay = Normalize(string.Join(' ', Label(model), model.ModelAlias, model.Model, model.Provider, model.DisplayName, model.Kind, HostOf(model)));
        return terms.All(term => hay.Contains(term, StringComparison.Ordinal));
    }

    /// <summary>Normalizes names so punctuation does not hide a match.</summary>
    /// <param name="text">Text to normalize.</param>
    /// <returns>Lowercase text without separators.</returns>
    private string Normalize(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : new string(text.ToLowerInvariant().Where(ch => !char.IsWhiteSpace(ch) && ch is not ('-' or '_' or '/' or '.')).ToArray());

    /// <summary>Reads the composer label for a model.</summary>
    /// <param name="model">The model.</param>
    /// <returns>Alias, short name, model id, or the reference's tail.</returns>
    private string Label(ModelChoice model)
    {
        if (!string.IsNullOrWhiteSpace(model.ModelAlias))
        {
            return model.ModelAlias.Trim();
        }

        if (!string.IsNullOrWhiteSpace(model.ModelShort))
        {
            return model.ModelShort.Trim();
        }

        return string.IsNullOrWhiteSpace(model.Model) ? ShortRef(model.Ref) : model.Model;
    }

    /// <summary>Drops the provider from a model reference.</summary>
    /// <param name="reference">Provider-qualified reference.</param>
    /// <returns>The model id.</returns>
    private string ShortRef(string reference)
    {
        var slash = reference.IndexOf('/');
        return slash >= 0 && slash + 1 < reference.Length ? reference[(slash + 1)..] : reference;
    }

    /// <summary>Describes a row with its model id, when aliased, and protocol.</summary>
    /// <param name="model">The model.</param>
    /// <returns>The second line, or an empty string.</returns>
    private string Describe(ModelChoice model)
    {
        var kind = KindLabel(model.Kind);
        var alias = model.ModelAlias?.Trim();
        if (!string.IsNullOrEmpty(alias) && !alias.Equals(model.Model, StringComparison.OrdinalIgnoreCase))
        {
            return kind.Length == 0 ? model.Model : model.Model + " 璺?" + kind;
        }

        return kind;
    }

    /// <summary>Localizes the protocol label the reference shows under a model.</summary>
    /// <param name="kind">Kernel protocol identity.</param>
    /// <returns>The label, or an empty string.</returns>
    private string KindLabel(string? kind) => kind switch
    {
        "responses" => "Responses",
        "openai" => MessageCatalog.Get("app.openai_compatible", _language),
        "anthropic" => MessageCatalog.Get("app.anthropic_compatible", _language),
        "gemini" => MessageCatalog.Get("app.gemini_native_experimental", _language),
        "typesafe" => MessageCatalog.Get("app.decision_system_one", _language),
        "extension" => MessageCatalog.Get("ext.extension", _language),
        _ => kind ?? string.Empty,
    };

    /// <summary>Gives an effort its human label.</summary>
    /// <param name="level">Kernel effort value.</param>
    /// <returns>The localized label, or the value itself.</returns>
    private string EffortLabel(string level) => EffortText(level);

    /// <summary>Gives an effort the wire spelling shown beside its label.</summary>
    /// <param name="level">Kernel effort value.</param>
    /// <returns>Auto, XHigh, or the capitalized value.</returns>
    private string EffortApi(string level) =>
        level.Equals("xhigh", StringComparison.OrdinalIgnoreCase)
            ? "XHigh"
            : char.ToUpperInvariant(level[0]) + level[1..].ToLowerInvariant();

    /// <summary>Explains what an effort level does.</summary>
    /// <param name="level">Kernel effort value.</param>
    /// <param name="forced">Whether thinking cannot be switched off.</param>
    /// <param name="cheapest">The lowest real level.</param>
    /// <returns>The localized description.</returns>
    private string EffortDescription(string level, bool forced, string cheapest)
    {
        var id = level.ToLowerInvariant();
        var known = s_effortLabels.ContainsKey(id);
        var text = known ? MessageCatalog.Get("composer.effort_desc." + id, _language) : level;
        if (forced && cheapest.Length > 0 && id == cheapest.ToLowerInvariant())
        {
            return text + " 璺?" + MessageCatalog.Get("composer.thinking_stays_on_and_is", _language);
        }

        return text;
    }

    /// <summary>Gives a model mode its label.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The localized label, or its id.</returns>
    private string ModeLabel(ModelModeChoice mode) => mode.LabelKey switch
    {
        "model_mode.pro" => MessageCatalog.Get("composer.pro_mode", _language),
        _ => mode.Id,
    };

    /// <summary>Explains what a model mode does.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The localized hint, or an empty string.</returns>
    private string ModeHint(ModelModeChoice mode) => mode.HintKey switch
    {
        "model_mode.pro.hint" => MessageCatalog.Get("composer.slower_the_model_does_more", _language),
        _ => string.Empty,
    };

    /// <summary>Picks the vendor slug a row draws.</summary>
    /// <param name="model">The model.</param>
    /// <returns>The row's mark.</returns>
    private string MarkOf(ModelChoice model)
    {
        if (!string.IsNullOrWhiteSpace(model.Mark))
        {
            return model.Mark;
        }

        return string.IsNullOrWhiteSpace(model.ServiceMark) ? model.Provider : model.ServiceMark;
    }

    /// <summary>Picks a stable tone for a vendor slug.</summary>
    /// <param name="name">Vendor slug or account label.</param>
    /// <returns>One of the account colours.</returns>
    private Color Tone(string name)
    {
        var hash = 0u;
        foreach (var ch in name)
        {
            hash = (hash * 31) + ch;
        }

        return s_tones[hash % (uint)s_tones.Length];
    }

    /// <summary>Reads a geometry resource.</summary>
    /// <param name="key">Resource key.</param>
    /// <returns>The geometry, or an empty path.</returns>
    private Geometry Geometry(string key) =>
        this.TryFindResource(key, out var value) && value is Geometry geometry ? geometry : new StreamGeometry();

    /// <summary>Reads a brush resource.</summary>
    /// <param name="key">Resource key.</param>
    /// <returns>The brush, or a muted fallback.</returns>
    private IBrush Brush(string key) =>
        this.TryFindResource(key, out var value) && value is IBrush brush ? brush : new SolidColorBrush(Color.Parse("#8A8F87"));

    /// <summary>One endpoint and credential slot in the catalog.</summary>
    /// <param name="Key">Stable account identity.</param>
    /// <param name="Label">Name on the heading.</param>
    /// <param name="Host">Endpoint host on the heading.</param>
    /// <param name="Mark">Vendor slug for the heading mark.</param>
    /// <param name="Models">Models listed under the heading.</param>
    private sealed record Account(string Key, string Label, string Host, string Mark, IReadOnlyList<ModelChoice> Models);
}
