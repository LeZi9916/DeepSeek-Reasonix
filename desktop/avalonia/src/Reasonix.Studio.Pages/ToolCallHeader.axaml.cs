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
    /// <param name="openStep">Whether the step begins expanded.</param><param name="openOutput">Whether the output begins unclipped.</param>
    /// <param name="rememberStep">Records an explicit step choice.</param><param name="rememberOutput">Records an explicit clip choice.</param>
    internal ToolCallHeader(ToolItem tool, InterfaceLanguage language, bool openStep, bool openOutput, Action<bool> rememberStep, Action<bool> rememberOutput)
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
        var status = this.FindControl<Path>("Status")!;
        status.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, StudioTypography.Resource(tool.Running ? "NetBrush" : string.IsNullOrEmpty(tool.Call.Err) ? "OkBrush" : "ErrInkBrush"));
        if (tool.Running || !string.IsNullOrEmpty(tool.Call.Err))
        {
            status.Data = Geometry.Parse(tool.Running ? "M12 3 A9 9 0 1 0 21 12" : "M6 6 18 18 M18 6 6 18");
        }

        var toggle = this.FindControl<Button>("Toggle")!;
        var all = this.FindControl<Button>("ExpandOutput")!;
        var cap = tool.Running || !string.IsNullOrEmpty(tool.Call.Err) ? 256d : 88d;
        AutomationProperties.SetName(toggle, tool.Call.Name);
        _output.PropertyChanged += (_, change) =>
        {
            if (change.Property == ScrollViewer.ExtentProperty || change.Property == IsVisibleProperty)
            {
                ShowMore();
            }
        };
        toggle.Click += (_, _) =>
        {
            SetOpen(!_output.IsVisible);
            _rememberStep(_output.IsVisible);
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

        void ShowMore() => all.IsVisible = _output.IsVisible && _output.Extent.Height > cap;
    }

    /// <summary>Shows the detail when find matches the tool output.</summary>
    internal void Reveal()
    {
        SetOpen(true);
        _rememberStep(true);
    }

    /// <summary>Updates the mounted detail and its disclosure mark.</summary>
    /// <param name="open">Whether the step is expanded.</param>
    private void SetOpen(bool open)
    {
        _output.IsVisible = open;
        this.FindControl<Path>("Chevron")!.Data = Geometry.Parse(open ? "M6 9 12 15 18 9" : "M9 6 15 12 9 18");
    }
}
