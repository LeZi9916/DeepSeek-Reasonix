using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The kernel status behind the task controls.</summary>
    private SessionStatus _composerStatus = new();

    /// <summary>Maps the kernel's approval identity to its localized label.</summary>
    /// <param name="mode">Kernel mode.</param>
    /// <returns>Message identity.</returns>
    private static string ApprovalKey(string mode) => mode switch
    {
        "dontAsk" => "safety.do_not_ask",
        "auto" => "safety.auto_approve",
        "yolo" => "safety.allow_all",
        _ => "safety.ask",
    };

    /// <summary>Wires the composer's task controls and responsive layout.</summary>
    private void InitializeComposeTools()
    {
        _hideAmounts = _workspacePreferences.HideAmounts;
        InitializeComposeMetrics();
        PlanMode.Click += (_, _) => ShowComposeModes(false);
        ApprovalMode.Click += (_, _) => ShowComposeModes(true);
        ComposeBranch.Click += async (_, _) => await ShowComposeBranchesAsync().ConfigureAwait(true);
        SideQuestion.Click += async (_, _) => await ToggleSideAsync().ConfigureAwait(true);
        CloseSide.Click += async (_, _) => await CloseSideQuestionAsync().ConfigureAwait(true);
        AskSide.Click += async (_, _) => await AskSideQuestionAsync().ConfigureAwait(true);
        QuoteSide.Click += (_, _) =>
        {
            var text = string.IsNullOrWhiteSpace(SideAnswer.SelectedText) ? SideAnswer.Text : SideAnswer.SelectedText;
            if (!string.IsNullOrWhiteSpace(text))
            {
                Composer.SelectedText = string.Join("\n", text.Split('\n').Select(line => "> " + line)) + "\n\n";
                Composer.Focus();
            }
        };
        RefinePrompt.Click += async (_, _) => await RefineDraftAsync().ConfigureAwait(true);
        DiscardRefine.Click += (_, _) => DismissRefinement();
        RetryRefine.Click += async (_, _) => await RefineDraftAsync().ConfigureAwait(true);
        AdoptRefine.Click += (_, _) =>
        {
            Composer.Text = _refinedText;
            Composer.CaretIndex = Composer.Text?.Length ?? 0;
            DismissRefinement();
            Composer.Focus();
        };
        HideAmounts.Click += (_, _) =>
        {
            _hideAmounts = !_hideAmounts;
            _workspacePreferences = _workspacePreferences with
            {
                HideAmounts = _hideAmounts,
            };
            if (_connectOnOpen)
            {
                _workspacePreferences.Save();
            }

            DrawComposeWallet();
            DrawComposeCards(Conversation.State);
        };
        Composer.AddHandler(KeyDownEvent, OnComposeReviewKey, RoutingStrategies.Tunnel);
        WalletButton.Click += async (_, _) =>
        {
            OpenSettings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await ShowSectionAsync("usage").ConfigureAwait(true);
        };
        ComposerArea.SizeChanged += (_, _) => LayoutComposeTools();
        SpeedText.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBlock.FontFamilyProperty || e.Property == TextBlock.FontSizeProperty || e.Property == TextBlock.FontWeightProperty)
            {
                LayoutComposeTools();
            }
        };
        RefineCard.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                UpdateComposeReviews();
            }
        };
        SideCard.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                UpdateComposeReviews();
            }
        };
        Closed += (_, _) =>
        {
            _refineCancellation?.Cancel();
            _sideCancellation?.Cancel();
        };
        LabelComposeTools();
    }

    /// <summary>Routes draft-review shortcuts before text submission.</summary>
    /// <param name="sender">The main input.</param>
    /// <param name="e">The key press.</param>
    private async void OnComposeReviewKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.E && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            e.Handled = true;
            await RefineDraftAsync().ConfigureAwait(true);
        }
        else if (e.Key == Key.Escape && RefineCard.IsVisible)
        {
            e.Handled = true;
            DismissRefinement();
        }
        else if (e.Key == Key.Escape && SideCard.IsVisible)
        {
            e.Handled = true;
            SideCard.IsVisible = false;
            SideQuestion.Classes.Remove("open");
            SideQuestion.Focus();
        }
    }

    /// <summary>Translates the task controls without replacing their content.</summary>
    private void LabelComposeTools()
    {
        Label(SideQuestion, "composer.side_question");
        Label(RefinePrompt, "composer.refine_prompt");
        Label(PlanMode, "composer.work_mode");
        Label(ComposeBranch, "composer.local_branches");
        Label(ApprovalMode, "safety.execution_permissions");
        Label(CacheToggle, "composer.open_the_session_overview");
        Label(SpeedToggle, "composer.view_generation_speed_details");
        Label(HideAmounts, _hideAmounts ? "composer.show_amounts" : "composer.hide_amounts");
        Label(WalletButton, "composer.view_wallet_balance");
        RefineTitle.Text = MessageCatalog.Get("composer.refined_prompt", Language);
        SideTitle.Text = MessageCatalog.Get("composer.side_question", Language);
        SideNote.Text = string.Join(" · ", new[] { "composer.side_badge_read_only", "composer.side_badge_not_main", "composer.side_badge_gone" }.Select(key => MessageCatalog.Get(key, Language)));
        CacheLabel.Text = MessageCatalog.Get("composer.cache", Language);
        ContextLabel.Text = MessageCatalog.Get("common.context", Language);
        DiscardRefine.Content = MessageCatalog.Get("composer.discard", Language);
        RetryRefine.Content = MessageCatalog.Get("common.retry", Language);
        AdoptRefine.Content = MessageCatalog.Get("composer.adopt", Language);
        AskSide.Content = MessageCatalog.Get("composer.ask_the_side_question", Language);
        QuoteSide.Content = MessageCatalog.Get("composer.side_quote_to_main", Language);
        Label(CloseSide, "composer.close_the_side_question");
        ShowComposeStatus(_composerStatus);
    }

    /// <summary>Projects kernel readback without touching unsent text.</summary>
    /// <param name="status">Authoritative task status.</param>
    private void ShowComposeStatus(SessionStatus status)
    {
        _composerStatus = status;
        PlanModeLabel.Text = status.Plan ? "Plan" : "Agent";
        ApprovalModeLabel.Text = MessageCatalog.Get(ApprovalKey(status.ToolApprovalMode), Language);
        EffortLabel.Text = status.Effort == "auto" ? MessageCatalog.Get("common.auto", Language) : status.Effort;
        ModelLabel.Text = string.IsNullOrEmpty(status.ModelRef) ? MessageCatalog.Get(MessageKey.Composer.ChooseModel, Language) : status.ModelRef[(status.ModelRef.IndexOf('/') + 1)..];
        ModelMark.IsVisible = status.ModelRef.StartsWith("deepseek/", StringComparison.OrdinalIgnoreCase);
        ToolTip.SetTip(ModelButton, status.ModelRef);
        LabelComposeLayout();
    }

    /// <summary>Offers task modes and keeps a refusal from changing the displayed mode.</summary>
    /// <param name="approval">Whether this is the approval chooser.</param>
    private void ShowComposeModes(bool approval)
    {
        var button = approval ? ApprovalMode : PlanMode;
        var menu = new MenuFlyout();
        var choices = approval ? new[] { "dontAsk", "ask", "auto", "yolo" } : new[] { "agent", "plan" };
        foreach (var value in choices)
        {
            var item = new MenuItem { Tag = value, Header = approval ? MessageCatalog.Get(ApprovalKey(value), Language) : value == "plan" ? "Plan" : "Agent" };
            var engine = _engine;
            var draft = _draftKey;
            item.Click += async (_, _) =>
            {
                if (engine is null || engine != _engine || draft != _draftKey)
                {
                    return;
                }

                button.IsEnabled = false;
                await TryAnswerAsync(async () =>
                {
                    if (approval)
                    {
                        await engine.Pages.SetApprovalModeAsync(value, _closing.Token).ConfigureAwait(true);
                    }
                    else
                    {
                        await engine.Pages.SetPlanAsync(value == "plan", _closing.Token).ConfigureAwait(true);
                    }

                    var status = await engine.StatusAsync(_closing.Token).ConfigureAwait(true);
                    if (status is not null && engine == _engine && draft == _draftKey)
                    {
                        ShowComposeStatus(status);
                    }
                }).ConfigureAwait(true);
                button.IsEnabled = true;
            };
            menu.Items.Add(item);
        }

        button.Flyout = menu;
        menu.ShowAt(button);
    }

    /// <summary>Refreshes the branch catalog when the chooser opens.</summary>
    /// <returns>The chooser read.</returns>
    private async Task ShowComposeBranchesAsync()
    {
        if (_engine is not { } engine)
        {
            return;
        }

        var draft = _draftKey;
        await TryAnswerAsync(async () =>
        {
            var branches = await engine.Pages.GitBranchesAsync(_closing.Token).ConfigureAwait(true);
            if (engine != _engine || draft != _draftKey)
            {
                return;
            }

            var menu = new MenuFlyout();
            foreach (var branch in branches["branches"]?.AsArray().OfType<System.Text.Json.Nodes.JsonObject>() ?? [])
            {
                var name = (string)branch["name"]!;
                var item = new MenuItem { Header = name, IsEnabled = (bool?)branch["current"] != true && string.IsNullOrEmpty((string?)branch["worktree"]) };
                item.Click += async (_, _) =>
                {
                    if (engine == _engine && draft == _draftKey)
                    {
                        await TryAnswerAsync(async () =>
                        {
                            var state = await engine.Pages.GitSwitchBranchAsync(name, _closing.Token).ConfigureAwait(true);
                            if (engine == _engine && draft == _draftKey)
                            {
                                ComposeBranchLabel.Text = (string?)state["branch"];
                                await LoadGitAsync().ConfigureAwait(true);
                                await ListFilesAsync().ConfigureAwait(true);
                            }
                        }).ConfigureAwait(true);
                    }
                };
                menu.Items.Add(item);
            }

            ComposeBranch.Flyout = menu;
            menu.ShowAt(ComposeBranch);
        }).ConfigureAwait(true);
    }

    /// <summary>Fits the toolbar to the conversation pane rather than the window width.</summary>
    private void LayoutComposeTools()
    {
        SpeedText.MinWidth = 5 * new FormattedText("0", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(SpeedText.FontFamily, SpeedText.FontStyle, SpeedText.FontWeight), SpeedText.FontSize, Brushes.Transparent).Width;
        var width = ComposerArea.Bounds.Width;
        ComposerArea.Classes.Set("compact", width <= 720);
        ComposerArea.Classes.Set("tight", width <= 590);
        ComposerArea.Classes.Set("tiny", width <= 420);
        ModelLabel.MaxWidth = width <= 720 ? Math.Clamp(width * 0.4, 84, 220) - 45 : 200;
        ComposeReviewScroll.MaxHeight = Math.Clamp(Bounds.Height * 0.35, 150, 320);
        Composer.MaxHeight = Math.Max(42, Math.Min(168, Bounds.Height * (ComposeReviewScroll.IsVisible ? 0.18 : 0.3)));
    }

    /// <summary>Shares the viewport budget between proposals and side questions.</summary>
    private void UpdateComposeReviews()
    {
        ComposeReviewScroll.IsVisible = RefineCard.IsVisible || SideCard.IsVisible;
        LayoutComposeTools();
    }

    /// <summary>Reserves the run header only when it has a visible reading.</summary>
    private void LabelComposeLayout()
    {
        var visible = ChangedFiles.IsVisible || Conversation.State.Running;
        ComposeRunState.IsVisible = visible;
        ComposeRows.RowDefinitions[0].Height = new GridLength(visible ? 30 : 0);
    }
}
