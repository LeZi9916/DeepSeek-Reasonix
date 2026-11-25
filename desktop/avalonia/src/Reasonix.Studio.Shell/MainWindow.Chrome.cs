using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;
using System;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The width chosen when the workspace column is docked.</summary>
    private double _railWidth = SkeletonMetrics.RailWidth;

    /// <summary>The explicit visibility choice at the current layout breakpoint.</summary>
    private bool? _railOpen;

    /// <summary>The previously laid-out workspace breakpoint.</summary>
    private bool? _compact;

    /// <summary>Gets the workspace width to restore after this window closes.</summary>
    internal double RailWidth => _railWidth;

    /// <summary>Configures native title-bar hit testing and the platform's window controls.</summary>
    internal void ConfigureNativeTitleBar()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows())
        {
            return;
        }

        ExtendClientAreaToDecorationsHint = true;
        var mac = OperatingSystem.IsMacOS();
        var height = mac ? 51 : 48;
        ExtendClientAreaTitleBarHeightHint = height;
        TitleChrome.Height = height;
        MainArea.RowDefinitions[0].Height = new GridLength(height);
        foreach (var bar in new Control[] { TitleChrome, SettingsTitleBar, SetupTitleBar })
        {
            WindowDecorationProperties.SetElementRole(bar, WindowDecorationsElementRole.TitleBar);
        }

        foreach (var action in new Control[] { OpenProject, ExpandRail, FindButton, ThemeSwitch, ChromeAnalysis, ViewSwitch, ChromeMore, CloseSettings })
        {
            WindowDecorationProperties.SetElementRole(action, WindowDecorationsElementRole.User);
        }

        if (mac)
        {
            BrandLockup.Margin = new Thickness(50, 0, 0, 0);
            CloseSettings.Margin = new Thickness(96, 0, 0, 0);
            SetupTitleBar.Padding = new Thickness(96, 0, 0, 0);
            LayoutRoot.SizeChanged += (_, _) => PadMacCaption();
            Rail.PropertyChanged += (_, change) =>
            {
                if (change.Property == IsVisibleProperty)
                {
                    PadMacCaption();
                }
            };
        }
        else
        {
            WindowDecorations = WindowDecorations.BorderOnly;
            WindowsCaption.IsVisible = true;
            TitleChrome.Padding = new Thickness(20, 0, 146, 0);
            WindowDecorationProperties.SetElementRole(WindowsCaption, WindowDecorationsElementRole.User);
            Label(Minimise, MessageKey.App.Minimize);
            Label(Maximise, MessageKey.App.Maximize);
            Label(CloseCaption, MessageKey.Common.Close);
            Minimise.Click += (_, _) => WindowState = WindowState.Minimized;
            Maximise.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            CloseCaption.Click += (_, _) => Close();
        }
    }

    /// <summary>Restores the stored workspace width and explicit theme.</summary>
    /// <param name="width">The saved dock width.</param>
    /// <param name="theme">An explicit light or dark appearance, or the system default.</param>
    /// <param name="reducedEffects">The local visual effects preference.</param>
    internal void RestoreChrome(double width, string? theme, bool reducedEffects)
    {
        ReducedEffects = reducedEffects;
        ApplyMotion();
        ResizeRail(width);
        RequestedThemeVariant = theme switch
        {
            "Dark" => ThemeVariant.Dark,
            "Light" => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };
    }

    /// <summary>Wires workspace navigation and the window's appearance controls.</summary>
    private void InitializeChrome()
    {
        Activated += (_, _) => ApplyMotion();
        CollapseRail.Click += (_, _) => ToggleRail();
        ExpandRail.Click += (_, _) => ToggleRail();
        RailGrip.DragDelta += (_, e) => ResizeRail(_railWidth + e.Vector.X);
        RailGrip.DoubleTapped += (_, _) => ResizeRail(SkeletonMetrics.RailWidth);
        RailGrip.KeyDown += OnGripKey;
        RailSearch.TextChanged += async (_, _) => await LoadTreeAsync().ConfigureAwait(true);
        InitializeFind();
        InitializeWorkbench();
        InitializeWorkbenchActions();
        InitializeChromeViews();
        LayoutRoot.SizeChanged += (_, _) =>
        {
            if (!_railMotionActive)
            {
                LayoutPanels();
            }
        };
        ThemeSwitch.Click += (_, _) =>
        {
            RequestedThemeVariant = ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
        };
        ActualThemeVariantChanged += (_, _) => LabelTheme();
        ConversationView.Click += (_, _) => ShowAnalysis(false);
        AnalysisView.Click += (_, _) => ShowAnalysis(true);
        InitializeEscapeStop();
        KeyDown += OnWindowKey;
        LabelChrome();
    }

    /// <summary>Translates the window controls in place.</summary>
    private void LabelChrome()
    {
        NewSessionLabel.Text = MessageCatalog.Get(MessageKey.Common.StartANewSession, Language);
        RailSearch.PlaceholderText = MessageCatalog.Get("session.search_sessions_and_actions", Language);
        Label(ExpandRail, MessageKey.App.ShowTheWorkspaceRail);
        Label(CollapseRail, MessageKey.Common.CollapseTheWorkspaceRail);
        Label(RailGrip, MessageKey.App.ResizeTheWorkspaceRail);
        Label(ContextToggle, MessageKey.Common.ContextDetails);
        DrawChromeViews();
        ContextTitle.Text = MessageCatalog.Get(MessageKey.Common.ContextWindow, Language);
        ConversationView.Header = MessageCatalog.Get(MessageKey.Feedback.Conversation, Language);
        AnalysisView.Header = MessageCatalog.Get(MessageKey.Common.RunAnalysis, Language);
        LabelTheme();
        LabelSidebar();
    }

    /// <summary>Updates the action's accessible name and pointer hint together.</summary>
    /// <param name="control">The interactive control.</param>
    /// <param name="key">Its message key.</param>
    private void Label(Control control, string key)
    {
        var label = MessageCatalog.Get(key, Language);
        AutomationProperties.SetName(control, label);
        ToolTip.SetTip(control, label);
    }

    /// <summary>Reserves the native macOS traffic lights when the workspace rail is folded.</summary>
    private void PadMacCaption()
    {
        TitleChrome.Padding = new Thickness(Rail.IsVisible ? 20 : 96, 0, 12, 0);
    }

    /// <summary>Labels the theme button with the appearance it will select.</summary>
    private void LabelTheme()
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        ThemeShape.Data = Avalonia.Media.Geometry.Parse(dark
            ? "M16,12 A4,4 0 1 1 8,12 A4,4 0 1 1 16,12 M12,2 V4 M12,20 V22 M2,12 H4 M20,12 H22 M4.93,4.93 L6.34,6.34 M17.66,17.66 L19.07,19.07 M4.93,19.07 L6.34,17.66 M17.66,6.34 L19.07,4.93"
            : "M20.99,13 A9,9 0 0 1 11,3 A9,9 0 1 0 21,13 Z");
        AutomationProperties.SetAutomationId(ThemeGlyph, dark ? "class:lucide lucide-sun studio-icon" : "class:lucide lucide-moon studio-icon");
        Label(ThemeSwitch, dark ? MessageKey.App.SwitchToLightTheme : MessageKey.App.SwitchToDarkTheme);
    }

    /// <summary>Changes the dock width without changing the room's breakpoint.</summary>
    /// <param name="width">The requested width, in logical pixels.</param>
    private void ResizeRail(double width)
    {
        _railWidth = Math.Clamp(width, 232, 440);
        LayoutPanels();
    }

    /// <summary>Opens or closes the workspace and leaves focus on the reachable toggle.</summary>
    private void ToggleRail()
    {
        var opening = !Rail.IsVisible;
        _railOpen = opening;
        if (!StartRailMotion(opening))
        {
            LayoutPanels();
            if (opening)
            {
                EnterView(Rail);
            }
        }

        (opening ? CollapseRail : ExpandRail).Focus();
    }

    /// <summary>Arranges the columns against the available logical viewport.</summary>
    private void LayoutPanels()
    {
        var room = LayoutRoot.Bounds.Size;
        if (room.Width <= 0)
        {
            return;
        }

        var compact = room.Width <= 1200;
        var drawer = room.Width <= 640;
        if (_compact != compact)
        {
            _compact = compact;
            _railOpen = null;
        }

        var open = _railOpen ?? !compact;
        var railMode = _workspacePreferences.NavigationRail;
        var icons = !drawer && (railMode == "on" || (railMode == "collapsed" && !open));
        NavigationRail.IsVisible = icons;
        var iconWidth = icons ? NavigationRail.Width : 0;
        Rail.IsVisible = open;
        Rail.Width = _railWidth;
        Rail.Opacity = open ? 1 : 0;
        Rail.Margin = new Thickness(iconWidth, 0, 0, 0);
        Rail.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumnSpan(Rail, drawer ? 2 : 1);
        RailGrip.IsVisible = open && !drawer;
        ExpandRail.IsVisible = !open;
        LayoutRoot.ColumnDefinitions[0].Width = new GridLength(iconWidth + (open && !drawer ? _railWidth : 0));
        QuickLinksFrame.Width = Math.Min(260, _railWidth - 20);
        if (!open)
        {
            QuickLinksPopup.IsOpen = false;
        }

        AnalysisPanel.Margin = room.Width <= 1120 ? new Thickness(20, 0) : new Thickness(0, 0, 10, 0);
        var stacked = room.Width <= 840;
        Grid.SetColumn(ContextPanel, stacked ? 0 : 2);
        LayoutWorkbench();
        Grid.SetRow(ContextPanel, stacked ? 1 : 0);
        ContextPanel.Width = stacked ? double.NaN : 316;
        ContextPanel.MaxHeight = stacked ? room.Height * 0.4 : double.PositiveInfinity;
        QueueScroll.MaxHeight = room.Height * 0.25;
    }

    /// <summary>Handles the keyboard equivalent of resizing and toggling the divider.</summary>
    /// <param name="sender">The divider.</param>
    /// <param name="e">The key press.</param>
    private void OnGripKey(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space)
        {
            ToggleRail();
        }
        else if (e.Key is Key.Left or Key.Right)
        {
            var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 48 : 12;
            ResizeRail(_railWidth + (e.Key == Key.Right ? step : -step));
        }
        else
        {
            return;
        }

        e.Handled = true;
    }

    /// <summary>Handles the desktop shortcuts shared across pages.</summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The key press.</param>
    private void OnWindowKey(object? sender, KeyEventArgs e)
    {
        var primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (SettingsPanel.IsVisible || SetupPanel.IsVisible)
        {
            if (e.Key == Key.Escape && _settings.IsConfirming)
            {
                _settings.ResolveConfirmation(false);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && SearchResultsPanel.IsVisible)
            {
                DismissSettingsSearch();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && (SettingsPanel.IsVisible || ConnectProvider.IsEnabled))
            {
                ReturnToConversation();
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.F && e.KeyModifiers == primary)
        {
            OpenFind();
        }
        else if (e.Key == Key.Escape && FindBar.IsVisible)
        {
            CloseFindBar();
        }
        else if (e.Key == Key.Escape && ComposerPlan.IsExpanded)
        {
            ComposerPlan.Collapse(restoreFocus: true);
        }
        else if (e.Key == Key.OemComma && e.KeyModifiers == primary)
        {
            OpenPreferences();
        }
        else if (e.Key == Key.K && e.KeyModifiers == primary)
        {
            OpenWorkspaceSearch();
        }
        else if (e.Key == Key.N && e.KeyModifiers == KeyModifiers.Alt)
        {
            OnNewRequested();
        }
        else if (e.Key == Key.B && e.KeyModifiers == primary)
        {
            ToggleRail();
        }
        else if (e.Key == Key.D1 && e.KeyModifiers == primary)
        {
            ShowAnalysis(false);
            if (WorkbenchPanel.IsVisible)
            {
                _ = ToggleChromeWorkbenchAsync();
            }
        }
        else if (e.Key == Key.Oem3 && e.KeyModifiers == primary)
        {
            TerminalAction.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        else if (e.Key is Key.Up or Key.Down && e.KeyModifiers == primary)
        {
            JumpToQuestion(e.Key == Key.Down);
        }
        else if (e.Key == Key.F && e.KeyModifiers == (primary | KeyModifiers.Shift) && WorkbenchPanel.IsVisible)
        {
            FullWorkbench.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        else if ((e.Key is Key.OemMinus or Key.Subtract or Key.OemPlus or Key.Add or Key.D0 or Key.NumPad0) &&
                 (e.KeyModifiers == primary || e.KeyModifiers == (primary | KeyModifiers.Shift)))
        {
            var step = e.Key switch
            {
                Key.OemMinus or Key.Subtract => -1,
                Key.OemPlus or Key.Add => 1,
                _ => 0,
            };
            _ = StepZoomAsync(step);
        }
        else if (e.Key == Key.D2 && e.KeyModifiers == primary && ChromeAnalysis.IsVisible)
        {
            ShowAnalysis(!AnalysisPanel.IsVisible);
        }
        else if (e.Key == Key.D3 && e.KeyModifiers == primary && ViewSwitch.IsVisible)
        {
            _ = ToggleChromeWorkbenchAsync();
        }
        else if (e.Key == Key.Escape && AnalysisPanel.IsVisible)
        {
            ShowAnalysis(false);
        }
        else if (e.Key == Key.Escape && Rail.IsVisible && LayoutRoot.Bounds.Width <= 640)
        {
            ToggleRail();
        }
        else if (e.Key == Key.Escape && ManualBrowserHost.IsEffectivelyVisible)
        {
            HideBrowser();
        }
        else if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && Conversation.State.Running && Stop.IsEnabled)
        {
            EscapeStop();
        }
        else
        {
            return;
        }

        e.Handled = true;
    }

    /// <summary>Switches the active pane while preserving the draft and reading position.</summary>
    /// <param name="show">Whether to display run metrics.</param>
    private void ShowAnalysis(bool show)
    {
        AnalysisPanel.IsVisible = show;
        ComposerArea.IsVisible = !show;
        if (show)
        {
            FindBar.IsVisible = false;
        }

        Conversation.IsVisible = !show;
        if (show)
        {
            DetachTerminal();
            WorkbenchPanel.IsVisible = false;
        }

        LayoutWorkbench();
        if (show)
        {
            _ = LoadAnalysisAsync();
        }
        else
        {
            Composer.Focus();
        }
    }
}
