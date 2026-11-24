using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State.Transcript;
using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Path = Avalonia.Controls.Shapes.Path;

namespace Reasonix.Studio.Pages;

/// <summary>A native tool disclosure driven by the canonical call, arguments and output.</summary>
internal sealed partial class ToolCallHeader : UserControl
{
    /// <summary>The retained detail of the tool.</summary>
    private readonly ScrollViewer _output;

    /// <summary>The manual step choice shared with the transcript.</summary>
    private readonly Action<bool> _rememberStep;

    /// <summary>Initializes a new instance of the <see cref="ToolCallHeader"/> class.</summary>
    /// <param name="tool">Canonical tool row.</param>
    /// <param name="language">Interface language.</param>
    /// <param name="inActivity">Whether the call is shown in the grouped step list.</param>
    /// <param name="openStep">Whether the step begins expanded.</param><param name="openOutput">Whether the output begins unclipped.</param>
    /// <param name="rememberStep">Records an explicit step choice.</param><param name="rememberOutput">Records an explicit clip choice.</param>
    internal ToolCallHeader(ToolItem tool, InterfaceLanguage language, bool inActivity, bool openStep, bool openOutput, Action<bool> rememberStep, Action<bool> rememberOutput)
    {
        AvaloniaXamlLoader.Load(this);
        _output = this.FindControl<ScrollViewer>("Output")!;
        _rememberStep = rememberStep;
        var name = this.FindControl<TextBlock>("ToolName")!;
        name.Text = tool.Call.Name == "edit_file" ? MessageCatalog.Get(MessageKey.Common.Edit, language) : tool.Call.Name;
        ToolTip.SetTip(name, tool.Call.Name);
        this.FindControl<SelectableTextBlock>("Result")!.Text = string.IsNullOrEmpty(tool.Call.Err) ? tool.Call.Output : tool.Call.Err + "\n" + tool.Call.Output;
        var argument = this.FindControl<TextBlock>("Argument")!;
        try
        {
            argument.Text = !string.IsNullOrEmpty(tool.Call.Args) && JsonNode.Parse(tool.Call.Args) is JsonObject args ? args["path"]?.ToString() ?? string.Empty : string.Empty;
        }
        catch (JsonException)
        {
            argument.Text = string.Empty;
        }

        ToolTip.SetTip(argument, tool.Call.Args);
        if (inActivity)
        {
            var glyph = this.FindControl<Path>("ToolGlyph")!;
            var kind = this.FindControl<Viewbox>("ToolKind")!;
            kind.Width = kind.Height = 14;
            glyph.StrokeThickness = 1.45;
            var icon = tool.Call.Name switch
            {
                "bash" or "bash_output" => "Glyph.terminal",
                "read_file" => "Glyph.file-text",
                "grep" => "Glyph.search",
                "ls" => "Glyph.folder",
                "web_fetch" => "Glyph.network",
                _ => "Glyph.edit",
            };
            glyph.Bind(Path.DataProperty, StudioTypography.Resource(tool.Call.Profile is null ? icon : "Glyph.git-fork"));
            var caption = tool.Call.Name switch
            {
                "bash" => "chat.run_command",
                "bash_output" => "chat.command_output",
                "read_file" => "chat.read_file",
                "write_file" => "chat.write_file",
                "edit_file" => "chat.edit_file",
                "grep" => "chat.search_code",
                "glob" => "safety.finding_files",
                "ls" => "chat.list_directory",
                _ => null,
            };
            if (caption is not null)
            {
                name.Text = MessageCatalog.Get(caption, language);
            }

            name.FontWeight = FontWeight.Normal;
            name.Bind(TextBlock.FontSizeProperty, StudioTypography.Size(13));
            argument.Text = ActivityDisclosure.ShortArgument(tool.Call.Args);
            argument.Bind(TextBlock.FontSizeProperty, StudioTypography.Size(12));
            argument.Bind(TextBlock.ForegroundProperty, StudioTypography.Resource("TextBrush"));
            this.FindControl<Grid>("HeaderGrid")!.ColumnDefinitions = new ColumnDefinitions("14,Auto,*,14");
            this.FindControl<Border>("Chevron")!.IsVisible = false;
        }

        if (tool.Call.Profile is { } profile)
        {
            if (inActivity)
            {
                name.Text = MessageCatalog.Get("chat.subtask", language);
            }

            var delegateName = profile.Name?.Trim();
            this.FindControl<TextBlock>("DelegateName")!.Text = delegateName;
            this.FindControl<Border>("DelegateLabel")!.IsVisible = !string.IsNullOrEmpty(delegateName);
        }

        InitializeBackgroundJob(tool, language, inActivity);

        var spins = inActivity && tool.Running;
        this.FindControl<Canvas>("RunningStatus")!.IsVisible = spins;
        this.FindControl<Viewbox>("StaticStatus")!.IsVisible = !spins;
        var status = this.FindControl<Path>("Status")!;
        status.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, StudioTypography.Resource(tool.Running ? "NetBrush" : string.IsNullOrEmpty(tool.Call.Err) ? "OkBrush" : "ErrInkBrush"));
        if (tool.Running || !string.IsNullOrEmpty(tool.Call.Err))
        {
            status.Data = Geometry.Parse(tool.Running ? "M12 3 A9 9 0 1 0 21 12" : "M6 6 18 18 M18 6 6 18");
        }

        var detail = this.FindControl<DisclosureMotion>("Detail")!;
        var toggle = this.FindControl<Button>("Toggle")!;
        var all = this.FindControl<Button>("ExpandOutput")!;
        var cap = tool.Running || !string.IsNullOrEmpty(tool.Call.Err) ? 256d : 88d;
        AutomationProperties.SetName(toggle, BackgroundJobId is null ? tool.Call.Name : name.Text);
        _output.PropertyChanged += (_, change) =>
        {
            if (change.Property == ScrollViewer.ExtentProperty)
            {
                ShowMore();
            }
        };
        detail.PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty)
            {
                ShowMore();
            }
        };
        toggle.Click += (_, _) =>
        {
            SetOpen(!detail.Expanded, animate: true);
            _rememberStep(detail.Expanded);
            ShowMore();
        };
        all.Click += (_, _) =>
        {
            openOutput = !openOutput;
            rememberOutput(openOutput);
            ApplyClip();
        };
        SetOpen(openStep);
        ApplyClip();

        void ApplyClip()
        {
            _output.MaxHeight = openOutput ? double.PositiveInfinity : cap;
            all.Content = MessageCatalog.Get(openOutput ? "common.collapse" : "chat.show_all", language);
            ShowMore();
        }

        void ShowMore() => all.IsVisible = detail.Expanded && _output.Extent.Height > cap;
    }

    /// <summary>Shows the detail when find matches the tool output.</summary>
    internal void Reveal()
    {
        SetOpen(true);
        _rememberStep(true);
    }

    /// <summary>Updates the mounted detail and its disclosure mark.</summary>
    /// <param name="open">Whether the step is expanded.</param>
    /// <param name="animate">Whether the reader toggled the row.</param>
    private void SetOpen(bool open, bool animate = false)
    {
        this.FindControl<DisclosureMotion>("Detail")!.SetExpanded(open, animate);
        this.FindControl<Border>("Chevron")!.Classes.Set("open", open);
    }
}
