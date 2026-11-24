using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell.Composer;
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
        PlanMode.IsVisible = _workspacePreferences.ShowModePicker;
        InitializeComposeMetrics();
        PlanMode.Click += (_, _) => ShowComposeModes(false);
        ApprovalMode.Click += (_, _) => ShowComposeModes(true);
        ModePanel.Picked += OnPlanPicked;
        PolicyPanel.Picked += OnApprovalPicked;
        PolicyPanel.BoundaryRequested += OnPolicyBoundary;
        BranchPanel.Picked += OnBranchPicked;
        BranchPanel.RetryRequested += OnBranchRetry;
        BranchPanel.IsolateRequested += OnIsolateRequested;
        BranchPanel.CloseRequested += BranchPopup.Close;
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
        UpdateAttachmentWarning();
        PlanModeLabel.Text = status.Plan ? "Plan" : "Agent";
        ApprovalModeLabel.Text = MessageCatalog.Get(ApprovalKey(status.ToolApprovalMode), Language);
        var risk = status.ToolApprovalMode == "yolo";
        ApprovalMode.Classes.Set("risk", risk);
        ApprovalShield.IsVisible = !risk;
        ApprovalWarningIcon.IsVisible = risk;
        var approvalName = MessageCatalog.Get("safety.execution_permissions", Language) + "：" + ApprovalModeLabel.Text;
        Avalonia.Automation.AutomationProperties.SetName(ApprovalMode, approvalName);
        ToolTip.SetTip(ApprovalMode, approvalName);
        if (string.IsNullOrEmpty(status.ModelRef))
        {
            ModelLabel.Text = MessageCatalog.Get(MessageKey.Composer.ChooseModel, Language);
            ModelMark.IsVisible = false;
            ModelInitial.IsVisible = false;
            EffortLabel.Text = ModelPicker.EffortText(status.Effort);
            ToolTip.SetTip(ModelButton, MessageCatalog.Get(MessageKey.Composer.ChooseModel, Language));
        }
        else
        {
            BindModelTrigger(status);
        }

        LabelComposeLayout();
    }

    /// <summary>Removes popovers when their owning conversation or view is replaced.</summary>
    private void CloseComposerMenus()
    {
        ModePopup.CloseImmediately();
        PolicyPopup.CloseImmediately();
        BranchPopup.CloseImmediately();
        ModelPicker.CloseImmediately();
    }

    /// <summary>Opens the self-drawn work-mode or permission panel.</summary>
    /// <param name="approval">Whether this is the approval chooser.</param>
    private void ShowComposeModes(bool approval)
    {
        if (!approval && IsComposerEditPending(ComposerEdit.Plan))
        {
            return;
        }

        var popup = approval ? PolicyPopup : ModePopup;
        if (popup.IsOpen)
        {
            popup.Close();
            return;
        }

        if (approval)
        {
            PolicyPanel.Show(_composerStatus.ToolApprovalMode, Language);
            popup.Anchor = ApprovalMode;
        }
        else
        {
            ModePanel.Show(_composerStatus.Plan, Language);
            popup.Anchor = PlanMode;
        }

        if (approval && ComposerArea.Classes.Contains("narrow-policy"))
        {
            popup.Show(ComposeFooter, ComposeFooter.Bounds.Width - 40);
        }
        else
        {
            popup.Show();
        }
    }

    /// <summary>Applies a work mode and keeps a refusal inside the open panel.</summary>
    /// <param name="mode">Chosen mode identity.</param>
    private async void OnPlanPicked(string mode)
    {
        if (_engine is not { } engine)
        {
            return;
        }

        var draft = _draftKey;
        var ticket = (engine, draft, ComposerEdit.Plan);
        if (!BeginComposerEdit(ticket))
        {
            return;
        }

        ModePopup.Close();
        PlanMode.Focus();
        try
        {
            await engine.Pages.SetPlanAsync(mode == "plan", _closing.Token).ConfigureAwait(true);
            var status = await engine.StatusAsync(_closing.Token).ConfigureAwait(true);
            if (status is not null && engine == _engine && draft == _draftKey)
            {
                ShowComposeStatus(status);
                ModePanel.Show(status.Plan, Language);
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (engine == _engine && draft == _draftKey)
            {
                ShowRefusal(error);
            }
        }
        finally
        {
            EndComposerEdit(ticket);
        }
    }

    /// <summary>Applies an approval mode and keeps a refusal inside the open panel.</summary>
    /// <param name="mode">Chosen kernel approval mode.</param>
    private async void OnApprovalPicked(string mode)
    {
        if (_engine is not { } engine)
        {
            return;
        }

        PolicyPanel.SetPending(true);
        try
        {
            var status = await ApplyApprovalAsync(engine, mode).ConfigureAwait(true);
            if (status is not null)
            {
                PolicyPanel.Show(status.ToolApprovalMode, Language);
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            PolicyPanel.ShowError(DescribeError(error));
        }
        finally
        {
            PolicyPanel.SetPending(false);
        }
    }

    /// <summary>Changes the approval preference and projects its readback on the same task.</summary>
    /// <param name="engine">The session receiving the edit.</param>
    /// <param name="mode">The selected approval mode.</param>
    /// <returns>The current task state, if still mounted.</returns>
    private async Task<SessionStatus?> ApplyApprovalAsync(Reasonix.Studio.Kernel.Session.SessionEngine engine, string mode)
    {
        var draft = _draftKey;
        await engine.Pages.SetApprovalModeAsync(mode, _closing.Token).ConfigureAwait(true);
        var status = await engine.StatusAsync(_closing.Token).ConfigureAwait(true);
        if (engine != _engine || draft != _draftKey)
        {
            return null;
        }

        status ??= _composerStatus with
        {
            ToolApprovalMode = mode,
        };
        ShowComposeStatus(status);
        return status;
    }

    /// <summary>Opens the sandbox settings destination from the permission panel.</summary>
    private async void OnPolicyBoundary()
    {
        PolicyPopup.Close();
        OpenPreferences();
        SelectSettingsNav("tools");
        await ShowSectionAsync("tools").ConfigureAwait(true);
        if (SettingsContent.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => Equals(control.Tag, "settings.sandbox")) is { } sandbox)
        {
            FocusSetting(sandbox);
        }
    }

    /// <summary>Refreshes the branch catalog when the chooser opens.</summary>
    /// <returns>The chooser read.</returns>
    private async Task ShowComposeBranchesAsync()
    {
        if (_engine is not { } engine || IsComposerEditPending(ComposerEdit.Branch))
        {
            return;
        }

        if (BranchPopup.IsOpen)
        {
            BranchPopup.Close();
            return;
        }

        var draft = _draftKey;
        BranchPopup.Anchor = ComposeBranch;
        try
        {
            var branches = await engine.Pages.GitBranchesAsync(_closing.Token).ConfigureAwait(true);
            if (engine != _engine || draft != _draftKey)
            {
                return;
            }

            if ((bool?)branches["repo"] == false)
            {
                BranchPanel.Show([], ComposeBranchLabel.Text ?? string.Empty, _composerStatus.WorkspaceRoot, Language);
                BranchPanel.ShowError(MessageCatalog.Get("composer.there_is_no_git_repository", Language));
                BranchPopup.Show();
                return;
            }

            var choices = (branches["branches"]?.AsArray().OfType<System.Text.Json.Nodes.JsonObject>() ?? [])
                .Select(branch => new BranchChoice(
                    (string?)branch["name"] ?? string.Empty,
                    (bool?)branch["current"] == true,
                    (string?)branch["worktree"] ?? string.Empty))
                .Where(branch => branch.Name.Length > 0)
                .ToList();
            WorktreeSources? sources = null;
            try
            {
                if ((await engine.Pages.WorkspaceInventoryAsync(_closing.Token).ConfigureAwait(true)).CanIsolate)
                {
                    sources = await engine.Pages.WorktreeSourcesAsync(_closing.Token).ConfigureAwait(true);
                }
            }
            catch (Exception)
            {
                sources = null;
            }

            BranchPanel.Show(choices, ComposeBranchLabel.Text ?? string.Empty, _composerStatus.WorkspaceRoot, Language, sources);
            BranchPopup.Show();
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            BranchPanel.Show([], ComposeBranchLabel.Text ?? string.Empty, _composerStatus.WorkspaceRoot, Language);
            BranchPanel.ShowError(DescribeError(error));
            BranchPopup.Show();
        }
    }

    /// <summary>Creates an isolated worktree and mounts its workspace.</summary>
    /// <param name="reference">Git branch ref chosen as the source.</param>
    private async void OnIsolateRequested(string reference)
    {
        if (_engine is not { } engine)
        {
            return;
        }

        var ticket = (engine, _draftKey, ComposerEdit.Branch);
        if (!BeginComposerEdit(ticket))
        {
            return;
        }

        BranchPanel.SetPending(reference);
        try
        {
            var attachments = _attachments
                .Where(chip => chip.SourcePath.Length > 0)
                .Select(chip => chip.SourcePath)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var root = await engine.Pages.IsolateWorkspaceAsync(_composerStatus.WorkspaceRoot, reference, attachments, _closing.Token).ConfigureAwait(true);
            if (engine != _engine || root.Length == 0)
            {
                return;
            }

            BranchPopup.Close();
            await OpenTargetCoreAsync(new SessionOpenTarget(string.Empty, root, string.Empty), false).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            BranchPanel.SetPending(null);
            BranchPanel.ShowError(DescribeError(error));
        }
        finally
        {
            EndComposerEdit(ticket);
        }
    }

    /// <summary>Switches to a branch and keeps a refusal inside the palette.</summary>
    /// <param name="name">Branch to switch to.</param>
    private async void OnBranchPicked(string name)
    {
        if (_engine is not { } engine)
        {
            return;
        }

        var ticket = (engine, _draftKey, ComposerEdit.Branch);
        if (!BeginComposerEdit(ticket))
        {
            return;
        }

        BranchPanel.SetPending(name);
        try
        {
            var state = await engine.Pages.GitSwitchBranchAsync(name, _closing.Token).ConfigureAwait(true);
            if (engine != _engine)
            {
                return;
            }

            await LoadGitAsync().ConfigureAwait(true);
            await ListFilesAsync().ConfigureAwait(true);
            ComposeBranchLabel.Text = (string?)state["branch"] ?? name;
            BranchPopup.Close();
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            BranchPanel.SetPending(null);
            BranchPanel.ShowError(DescribeError(error));
        }
        finally
        {
            EndComposerEdit(ticket);
        }
    }

    /// <summary>Reads the branches again after a failed palette read.</summary>
    private async void OnBranchRetry()
    {
        BranchPopup.Close();
        await ShowComposeBranchesAsync().ConfigureAwait(true);
    }

    /// <summary>Fits the toolbar to the conversation pane rather than the window width.</summary>
    private void LayoutComposeTools()
    {
        SpeedText.MinWidth = 5 * new FormattedText("0", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(SpeedText.FontFamily, SpeedText.FontStyle, SpeedText.FontWeight), SpeedText.FontSize, Brushes.Transparent).Width;
        var width = ComposerArea.Bounds.Width;
        ComposerArea.Classes.Set("compact", width <= 720);
        ComposerArea.Classes.Set("tight", width <= 590);
        ComposerArea.Classes.Set("tiny", width <= 420);
        ArrangeComposeFooter(width);
        ModelLabel.MaxWidth = width <= 720 ? Math.Clamp(width * 0.4, 84, 220) - 45 : 200;
        ComposeReviewScroll.MaxHeight = Math.Clamp(Bounds.Height * 0.35, 150, 320);
        Composer.MaxHeight = Math.Max(42, Math.Min(168, Bounds.Height * (ComposeReviewScroll.IsVisible ? 0.18 : 0.3)));
    }

    /// <summary>Fits the send and stop actions beside wrapping tools in a docked conversation.</summary>
    /// <param name="width">The conversation's composer width.</param>
    private void ArrangeComposeFooter(double width)
    {
        var narrow = width is > 0 and <= 264;
        var narrowRun = narrow && Conversation.State.Running;
        var wasNarrow = ComposerArea.Classes.Contains("narrow-policy");
        var wasNarrowRun = NarrowRunningModel.IsVisible;
        if (narrow != wasNarrow || narrowRun != wasNarrowRun)
        {
            var approvalHost = narrow && !narrowRun ? (Panel)NarrowComposeControls : ComposeTools;
            var modelHost = narrowRun ? NarrowRunningModel : narrow ? NarrowComposeControls : ComposeFooter;
            Move(ApprovalMode, approvalHost);
            Move(ComposeModel, modelHost);
            Grid.SetColumn(ComposeModel, narrowRun ? 0 : 1);
            ComposeModel.HorizontalAlignment = narrowRun ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            NarrowComposeControls.IsVisible = narrow && !narrowRun;
            NarrowRunningModel.IsVisible = narrowRun;
            ComposerArea.Classes.Set("narrow-policy", narrow);
            ComposeFooter.RowDefinitions[0].Height = narrow ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            ComposeFooter.RowDefinitions[1].Height = narrow && !narrowRun ? GridLength.Auto : new GridLength(0);
            ComposeFooter.RowDefinitions[2].Height = narrowRun ? GridLength.Auto : new GridLength(0);
            ComposeRows.RowDefinitions[Grid.GetRow(ComposeFooter)].Height = narrow ? GridLength.Auto : new GridLength(46);
            DrawQueue(Conversation.State);
        }

        Send.Width = narrowRun
            ? QueueDuringRun ? 97 : 71
            : Conversation.State.Running ? double.NaN : 32;
        var reserved = narrowRun
            ? ComposeFooter.Margin.Left + ComposeFooter.Margin.Right + Stop.Width + Send.Width + Stop.Margin.Left + Send.Margin.Left + (3 * ComposeFooter.ColumnSpacing)
            : 54;
        ComposeTools.MaxWidth = narrow ? Math.Max(22, width - reserved) : double.PositiveInfinity;
        ComposeModel.MaxWidth = narrow && !narrowRun ? Math.Max(35, width - 90) : double.PositiveInfinity;

        static void Move(Control control, Panel destination)
        {
            if (ReferenceEquals(control.Parent, destination))
            {
                return;
            }

            if (control.Parent is Panel previous)
            {
                previous.Children.Remove(control);
            }

            destination.Children.Add(control);
        }
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
        var visible = ChangedFiles.IsVisible || ComposerRunBadge.IsVisible;
        ComposerRunTokens.IsVisible = ComposerRunBadge.IsVisible && !ChangedFiles.IsVisible && HasRunTokens(Conversation.State);
        if (!ComposerRunTokens.IsVisible)
        {
            ComposerRunTokens.CancelFeedback();
        }

        ComposeRunState.IsVisible = visible;
        ComposeRows.RowDefinitions[0].Height = new GridLength(visible ? 30 : 0);
    }
}
